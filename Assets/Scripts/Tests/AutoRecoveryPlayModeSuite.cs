#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Config;
using Core.Data;
using Core.Events;
using Core.Generated;
using Core.Services;
using Core.Services.Common;
using Core.Services.DataDictionary;
using Core.Services.Inventory;
using Core.Services.NewWords;
using Core.UI;
using Cysharp.Threading.Tasks;
using Game.Logic;
using UI.Components;
using UI.Popups;
using UI.Screens;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using Zenject;
using Object = UnityEngine.Object;

namespace Tests.AutoRecovery
{
    // Editor-only integration suite. The coordinator selects an empty scene BEFORE Play Mode.
    // Real controllers, events, field operations, AI, animations and recovery storage are used.
    // Only external services (inventory server, profile, audio/skins and popup routing) are isolated.
    public static class AutoRecoveryPlayModeSuite
    {
        public const string ScenePath = "Assets/Scenes/Tests/AutoRecoveryTests.unity";
        public const string ActiveKey = "Wordlet.RecoveryTests.Active";
        public const string RootKey = "Wordlet.RecoveryTests.Root";
        public const string StageKey = "Wordlet.RecoveryTests.Stage";
        public const string DoneKey = "Wordlet.RecoveryTests.Done";
        public const string FailedKey = "Wordlet.RecoveryTests.Failed";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static Fixture _fixture;
        private static string _root;
        private static int _checks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnPlayModeStarted()
        {
            if (SessionState.GetBool(ActiveKey, false)) RunAsync().Forget();
        }

        private static async UniTaskVoid RunAsync()
        {
            _root = SessionState.GetString(RootKey, "");
            _checks = 0;
            bool originalBackground = Application.runInBackground;
            var unexpectedErrors = new List<string>();
            void OnLog(string message, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) unexpectedErrors.Add(message);
            }
            Application.logMessageReceived += OnLog;
            try
            {
                if (SceneManager.GetActiveScene().path != ScenePath || Object.FindAnyObjectByType<Core.Installers.CoreInstaller>() != null)
                    throw new InvalidOperationException("Refusing tests outside the isolated scene.");
                string expectedRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../.utmp/AutoRecoveryTests"));
                if (!Path.GetFullPath(_root).StartsWith(expectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refusing non-test storage path.");
                Directory.CreateDirectory(_root);
                // Native runtime flag only; never write PlayerSettings or the user's preferences.
                Application.runInBackground = true;
                await LocalizationSettings.InitializationOperation.Task;
                if (SessionState.GetInt(StageKey, 1) == 1) await FirstSession();
                else await SecondSession();
                Record("PASS", "Session completed: " + _checks + " assertions; no production startup or PlayFab calls.");
            }
            catch (Exception exception)
            {
                SessionState.SetBool(FailedKey, true);
                Record("FAIL", exception.ToString());
                Debug.LogError("[RecoveryTests] " + exception);
            }
            finally
            {
                if (_fixture != null) { await _fixture.DisposeAsync(); _fixture = null; }
                if (unexpectedErrors.Count > 0)
                {
                    SessionState.SetBool(FailedKey, true);
                    Record("FAIL", "Unexpected Console errors: " + string.Join("; ", unexpectedErrors));
                }
                Application.logMessageReceived -= OnLog;
                Application.runInBackground = originalBackground;
                SessionState.SetBool(DoneKey, true);
            }
        }

        private static void Record(string status, string description)
        {
            string line = "- " + status + ": " + description;
            Debug.Log("[RecoveryTests] " + line);
            File.AppendAllText(Path.Combine(_root, "report.md"), line + Environment.NewLine);
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            ++_checks;
            Record("PASS", description);
        }

        private static async UniTask NewRound(SaveGameData saved = null, bool recovery = false, string slot = "current")
        {
            if (_fixture != null) await _fixture.DisposeAsync();
            _fixture = new Fixture(Path.Combine(_root, slot));
            await _fixture.StartAsync(saved, recovery);
        }

        private static async UniTask FirstSession()
        {
            await BoosterSeparationCases();
            await NewRound();
            var initial = _fixture.Load();
            Check(initial != null && initial.playerTurn && !initial.hasPlacedLetter && initial.playerScore == 0, "A: initial checkpoint exists before any move");
            _fixture.SaveTo(initial, Path.Combine(_root, "restart-initial"));

            await _fixture.Erase();
            Check(_fixture.Load().eraserUsedThisTurn && !_fixture.Load().eraserRemovalCommitted, "Eraser starts reversible in the actual game");
            await _fixture.MissingWord();
            Check(_fixture.Inventory.GetQuantity(BoosterType.Eraser) == 20 && _fixture.Board()[10] == "S", "Eraser + missing word restores letter and refunds exactly once");
            Check(_fixture.Network.Refunds(BoosterType.Eraser) == 1 && !_fixture.Load().eraserUsedThisTurn, "Rollback clears refund replay state in checkpoint");

            await NewRound();
            await _fixture.Erase();
            await _fixture.Use(BoosterType.Mixer);
            var mixed = _fixture.Load();
            Check(mixed.eraserRemovalCommitted && mixed.eraserUsedThisTurn && string.IsNullOrEmpty(mixed.erasedLetter), "Successful real mixer commits eraser and clears rollback payload");
            string[] mixedBoard = _fixture.Board();
            await _fixture.MissingWord();
            Check(_fixture.Board().SequenceEqual(mixedBoard) && _fixture.Network.Refunds(BoosterType.Eraser) == 0, "Eraser + mixer + missing word preserves mixed board without refund");
            int consumes = _fixture.Network.Consumes;
            await _fixture.Use(BoosterType.Eraser);
            Check(_fixture.Network.Consumes == consumes && _fixture.Inventory.GetQuantity(BoosterType.Eraser) == 19, "Committed eraser remains limited to one use per turn");
            await NewRound(mixed, true);
            Check(_fixture.Network.Consumes == 0 && _fixture.Network.TotalRefunds == 0 && _fixture.Load().eraserRemovalCommitted, "Recovery restores committed eraser without server transactions");
            await _fixture.MissingWord();
            Check(_fixture.Board().SequenceEqual(mixed.boardRows) && _fixture.Network.Refunds(BoosterType.Eraser) == 0, "Eraser + mixer + recovery + missing word cannot restore old cell");

            await NewRound();
            await _fixture.Erase();
            await _fixture.Use(BoosterType.Swap);
            EventBus.Raise(new LetterSelectEvent { letter = _fixture.Cells[11] });
            EventBus.Raise(new LetterSelectEvent { letter = _fixture.Cells[12] });
            Check(_fixture.Load().eraserRemovalCommitted && _fixture.Board()[11] == "O" && _fixture.Board()[12] == "T", "Real completed swap commits eraser");
            var swappedBoard = _fixture.Board();
            await _fixture.MissingWord();
            Check(_fixture.Board().SequenceEqual(swappedBoard) && _fixture.Network.Refunds(BoosterType.Eraser) == 0, "Eraser + swap + missing word preserves exchange");

            await NewRound();
            await _fixture.Erase();
            await _fixture.Use(BoosterType.Swap);
            EventBus.Raise(new LetterSelectEvent { letter = _fixture.Cells[11] });
            await _fixture.CloseSelection();
            Check(!_fixture.Load().swapSelectionPending && !_fixture.Load().eraserRemovalCommitted && _fixture.Network.Refunds(BoosterType.Swap) == 1, "Cancelling a partially selected swap saves closed mode and refunds swap");
            await _fixture.MissingWord();
            Check(_fixture.Board()[10] == "S" && _fixture.Network.Refunds(BoosterType.Eraser) == 1, "Cancelled swap keeps normal eraser rollback");

            await NewRound();
            await _fixture.Erase();
            await _fixture.Use(BoosterType.Hint);
            Check(_fixture.Load().hasPlacedLetter && _fixture.Load().eraserRemovalCommitted, "Actual Hint booster commits eraser");
            EventBus.Raise(new GameCancelEvent());
            var hintCancelled = _fixture.Board();
            Check(!_fixture.Load().hasPlacedLetter && _fixture.Load().eraserRemovalCommitted, "Cancelling successful hint preserves eraser commitment");
            await _fixture.MissingWord();
            Check(_fixture.Board().SequenceEqual(hintCancelled) && _fixture.Network.Refunds(BoosterType.Eraser) == 0, "Eraser + letter + cancellation + missing word does not refund eraser");

            await NewRound();
            await _fixture.Erase();
            await _fixture.Use(BoosterType.Slowdown);
            Check(_fixture.Load().slowdownRemainingSeconds > 0 && !_fixture.Load().eraserRemovalCommitted, "Slowdown persists timer effect without committing eraser");
            await _fixture.MissingWord();
            Check(_fixture.Board()[10] == "S" && _fixture.Network.Refunds(BoosterType.Eraser) == 1, "Eraser + slowdown + missing word rolls eraser back");
            _fixture.Booster.StopSlowdown();
            Check(_fixture.Load().slowdownRemainingSeconds == 0, "Cancelled slowdown is absent from checkpoint");
            long stoppedTicks = _fixture.Load().savedAtUtcTicks;
            _fixture.Booster.StopSlowdown();
            Check(_fixture.Load().savedAtUtcTicks == stoppedTicks, "Repeated slowdown stop does not write a duplicate checkpoint");
            await _fixture.Use(BoosterType.Slowdown);
            await UniTask.Delay(1300);
            Check(_fixture.Load().slowdownRemainingSeconds == 0 && !_fixture.Booster.IsSlowdownActive, "Expired slowdown clears checkpoint even if visual countdown finishes first");
            var expired = _fixture.Load();
            await NewRound(expired, true);
            Check(!_fixture.Booster.IsSlowdownActive && _fixture.Load().slowdownRemainingSeconds == 0 && _fixture.Network.Consumes == 0, "Recovery cannot restart an already finished slowdown");

            await NewRound();
            await _fixture.Erase();
            File.WriteAllText(_fixture.RecoveryPath, SaveGameStorage.Serialize(_fixture.Load()).Replace("\"eraserRemovalCommitted\":false,", ""));
            var reversible = _fixture.Load();
            await NewRound(reversible, true);
            Check(_fixture.Network.Consumes == 0 && !_fixture.Load().eraserRemovalCommitted && _fixture.Load().erasedLetter == "S", "Legacy recovery before a second booster keeps eraser reversible without re-consumption");
            await _fixture.MissingWord();
            Check(_fixture.Board()[10] == "S" && _fixture.Network.Refunds(BoosterType.Eraser) == 1, "Recovered reversible eraser refunds once");

            await NewRound();
            await _fixture.Use(BoosterType.Eraser);
            await _fixture.CloseSelection();
            Check(!_fixture.Load().eraserSelectionPending && !_fixture.Load().eraserUsedThisTurn && _fixture.Network.Refunds(BoosterType.Eraser) == 1, "Cancelling eraser selection saves non-pending state before refund");

            // A one-letter board guarantees a genuine mixer failure after cancelling a provisional move.
            var sparse = _fixture.Game.GetGameData(false);
            sparse.boardRows = Enumerable.Repeat("", 25).ToArray();
            sparse.boardRows[12] = "A";
            sparse.eraserUsedThisTurn = false;
            sparse.eraserSelectionPending = false;
            await NewRound(sparse);
            _fixture.Place("Q");
            _fixture.Checkpoint();
            _fixture.Network.FailRefund = true;
            await _fixture.Use(BoosterType.Mixer);
            Check(!_fixture.Load().hasPlacedLetter && _fixture.Load().boardRows.Count(x => !string.IsNullOrEmpty(x)) == 1, "Failed mixer + failed refund saves cancelled provisional letter");
            Check(_fixture.Network.Refunds(BoosterType.Mixer) == 1, "Failed refund is attempted once and not queued by recovery");
            long noChangeTicks = _fixture.Load().savedAtUtcTicks;
            _fixture.Network.FailRefund = false;
            await _fixture.Use(BoosterType.Mixer);
            Check(_fixture.Load().savedAtUtcTicks == noChangeTicks, "Failed mixer without a cancelled letter does not write redundant checkpoint");
            var failedMixer = _fixture.Load();
            await NewRound(failedMixer, true);
            Check(!_fixture.Game.GetGameData(false).hasPlacedLetter && !_fixture.Board().Contains("Q") && _fixture.Network.TotalRefunds == 0, "Recovery after failed mixer does not resurrect cancellation or retry refund");

            await NewRound();
            _fixture.OnlyUsedWordInDictionary();
            _fixture.Place("Q");
            _fixture.Checkpoint();
            _fixture.Network.FailRefund = true;
            await _fixture.Use(BoosterType.Hint);
            Check(!_fixture.Load().hasPlacedLetter && !_fixture.Load().boardRows.Contains("Q"), "Failed real word search + failed refund saves cancelled provisional letter");
            noChangeTicks = _fixture.Load().savedAtUtcTicks;
            await _fixture.Use(BoosterType.Hint);
            Check(_fixture.Load().savedAtUtcTicks == noChangeTicks, "Failed letter search without state changes does not rewrite checkpoint");
            var failedLetter = _fixture.Load();
            await NewRound(failedLetter, true);
            Check(!_fixture.Game.GetGameData(false).hasPlacedLetter && !_fixture.Board().Contains("Q") && _fixture.Network.TotalRefunds == 0, "Recovery after failed letter search does not resurrect cancellation or retry refund");

            await NewRound();
            _fixture.Place("Q");
            _fixture.Checkpoint();
            var beforeDenial = _fixture.Load();
            _fixture.Network.FailConsume = true;
            await _fixture.Use(BoosterType.Mixer);
            Check(_fixture.Load().savedAtUtcTicks == beforeDenial.savedAtUtcTicks && _fixture.Game.GetGameData(false).hasPlacedLetter, "Rejected consumption leaves provisional move and checkpoint unchanged");
            EventBus.Raise(new GameCancelEvent());
            Check(!_fixture.Load().hasPlacedLetter && !_fixture.Load().boardRows.Contains("Q"), "Ordinary cancellation updates recovery");

            // Real player acceptance, scale animation, score/history update and turn switch.
            _fixture.Network.FailConsume = false;
            await _fixture.Use(BoosterType.Hint);
            _fixture.SelectHintPath();
            _fixture.Config.Game.delayAIPlaySeconds = 30;
            EventBus.Raise(new GameGoEvent());
            await Wait(() => !_fixture.Game.GetGameData(false).playerTurn, "player turn completion");
            var aiTurn = _fixture.Load();
            Check(!aiTurn.playerTurn && aiTurn.playerWords.Count == 1 && aiTurn.playerScore == aiTurn.playerWords[0].Length && aiTurn.lastWord == aiTurn.playerWords[0], "B: accepted player word saves board, score, history and AI ownership after animation");
            _fixture.SaveTo(aiTurn, Path.Combine(_root, "restart-ai"));

            await NewRound();
            await _fixture.Game.ExitRoundAsync(saveManually: true);
            Check(!_fixture.Recovery.HasRecoverySave && _fixture.Manual.HasSave(), "D: intentional exit clears recovery and preserves isolated manual save");
            ((IGameBoosterHost)_fixture.Game).SaveRecoveryCheckpoint();
            Check(!_fixture.Recovery.HasRecoverySave, "Exit prevents late checkpoint resurrection");

            await NewRound();
            var legacy = _fixture.Load();
            string legacyJson = SaveGameStorage.Serialize(legacy).Replace("\"eraserRemovalCommitted\":false,", "");
            File.WriteAllText(_fixture.RecoveryPath, legacyJson);
            Check(_fixture.Load() != null, "E: legacy checkpoint without commit field loads through real storage validator");
            _fixture.Manual.SaveLegacy(legacy);
            var legacyManual = await _fixture.Manual.LoadAsync();
            await NewRound(legacyManual);
            Check(_fixture.Board().SequenceEqual(legacyManual.boardRows) && !_fixture.Game.GetGameData(false).eraserUsedThisTurn
                && !_fixture.Game.GetGameData(false).hasPlacedLetter, "E: actual game startup restores legacy manual data without booster fields");
            File.WriteAllText(_fixture.RecoveryPath, "{broken json");
            Check(_fixture.Load() == null && !_fixture.Recovery.HasRecoverySave, "E: corrupted recovery is rejected and removed only from test slot");

            var finishingRound = _fixture.LoadFrom(Path.Combine(_root, "restart-initial"));
            await NewRound(finishingRound, true);
            _fixture.Manual.SaveLegacy(_fixture.Load());
            Set(_fixture.Game, "_maxPasses", (uint)1);
            EventBus.Raise(new TimeExpiredEvent());
            await UniTask.Yield();
            Check(!_fixture.Recovery.HasRecoverySave, "D: actual round finish removes recovery");
            Check(_fixture.Manual.HasSave(), "D: finishing a recovered round leaves independent manual save intact");
            ((IGameBoosterHost)_fixture.Game).SaveRecoveryCheckpoint();
            Check(!_fixture.Recovery.HasRecoverySave, "Finished round cannot recreate checkpoint");
            await CancellationConsistencyCases();
        }

        private static async UniTask RepeatedWordRound()
        {
            await NewRound();
            var seed = _fixture.Load();
            seed.playerWords.Add("AS");
            seed.playerScore = 2;
            seed.lastWord = "AS";
            await _fixture.RestartRound(seed);
        }

        private static async UniTask BoosterSeparationCases()
        {
            Check((int)BoosterType.None == 0 && (int)BoosterType.Letter == 1 && (int)BoosterType.Slowdown == 2
                && (int)BoosterType.Eraser == 3 && (int)BoosterType.Mixer == 4 && (int)BoosterType.Swap == 5 && (int)BoosterType.Hint == 6,
                "Booster IDs preserve all legacy serialized values; Hint is 6");
            Check(PlayFabInventoryKeys.ToKey(BoosterType.Letter) == "boost_letter" && PlayFabInventoryKeys.ToKey(BoosterType.Hint) == "boost_hint"
                && PlayFabInventoryKeys.GetKeys().Distinct().Count() == 6, "Letter and Hint have independent server inventory keys");
            Check(RewardedBoosterCatalog.All.Count == 5 && !RewardedBoosterCatalog.TryGetPlayFabKey(BoosterType.Hint, out _),
                "Hint is excluded from the existing rewarded-ad catalog");

            await NewRound();
            var buttons = _fixture.Screen.BoosterPanel.GetComponentsInChildren<BoosterUI>(true);
            Check(buttons.Length == 6 && buttons.Select(x => x.Type).Distinct().Count() == 6,
                "Actual gameplay prefab has six distinct booster buttons");
            // A restrictive Letter config must not cap Hint's longest-word search.
            _fixture.Config.Game.boosterLetterAiSettings.WordLength.Max = 2;
            await _fixture.ClickBooster(BoosterType.Hint);
            var hint = _fixture.Game.GetGameData(false);
            int placed = _fixture.Cells.FindIndex(x => (bool)Get(x, "_isClue"));
            Check(hint.playerTurn && hint.playerWords.Count == 0 && hint.hasPlacedLetter && placed >= 0
                && _fixture.SelectedIndexes.Count == 0 && _fixture.Screen.GetTextWord() == "",
                "Hint click places only its clue letter without selecting or accepting the word");
            Check(_fixture.Cells.Where(x => x.Index != placed).All(x => Get(x, "_highlightState").ToString() == "None"),
                "Hint never highlights the rest of its word");
            Check(_fixture.Inventory.GetQuantity(BoosterType.Hint) == 19 && _fixture.Inventory.GetQuantity(BoosterType.Letter) == 20,
                "Hint click consumes Hint only");
            var hintEvent = _fixture.Analytics.Events.Single(x => x.Name == AnalyticsEvents.BoosterUsage.HintBoosterSuccess);
            Check(Convert.ToInt32(hintEvent.Parameters[AnalyticsEvents.Parameter.WordLength]) > 2
                && !_fixture.Analytics.Events.Any(x => x.Name == AnalyticsEvents.BoosterUsage.LetterBoosterSuccess),
                "Hint searches beyond Letter limits and logs its own success event");
            EventBus.Raise(new LetterSelectEvent { letter = _fixture.Cells[placed] });
            EventBus.Raise(new LetterBacktrackEvent());
            Check(Get(_fixture.Cells[placed], "_highlightState").ToString() == "SelectedCell", "Hint highlight survives path backtracking");
            EventBus.Raise(new GameCancelEvent());
            Check(!_fixture.Game.GetGameData(false).hasPlacedLetter && _fixture.Board()[placed] == ""
                && !(bool)Get(_fixture.Cells[placed], "_isClue") && _fixture.Inventory.GetQuantity(BoosterType.Hint) == 19,
                "Normal Cancel removes clue without refunding the consumed Hint");

            await NewRound();
            _fixture.Config.Game.delayAIPlaySeconds = 30;
            await _fixture.ClickBooster(BoosterType.Letter);
            var letter = _fixture.Game.GetGameData(false);
            Check(!letter.playerTurn && letter.playerWords.Count == 1 && letter.playerScore == letter.playerWords[0].Length,
                "Letter click restores pre-prototype auto-selection and auto-acceptance");
            Check(_fixture.Inventory.GetQuantity(BoosterType.Letter) == 19 && _fixture.Inventory.GetQuantity(BoosterType.Hint) == 20,
                "Letter click consumes Letter only");
            Check(_fixture.Analytics.Events.Any(x => x.Name == AnalyticsEvents.BoosterUsage.LetterBoosterSuccess)
                && !_fixture.Analytics.Events.Any(x => x.Name == AnalyticsEvents.BoosterUsage.HintBoosterSuccess),
                "Letter keeps its original analytics identity");

            foreach (var type in new[] { BoosterType.Letter, BoosterType.Hint })
            {
                await NewRound();
                _fixture.OnlyUsedWordInDictionary();
                await _fixture.Use(type);
                Check(_fixture.Inventory.GetQuantity(BoosterType.Letter) == 20 && _fixture.Inventory.GetQuantity(BoosterType.Hint) == 20
                    && _fixture.Network.Refunds(type) == 1 && _fixture.Network.TotalRefunds == 1,
                    type + " failed search refunds only the activated booster");
            }
        }

        private static async UniTask CancellationConsistencyCases()
        {
            await RepeatedWordRound();
            await _fixture.Erase(14);
            await _fixture.RepeatedWord();
            var duplicate = _fixture.Load();
            Check(!duplicate.hasPlacedLetter && duplicate.boardRows[5] == "" && _fixture.Board().SequenceEqual(duplicate.boardRows), "Cancel A: real repeated-word rejection saves the cancelled letter");
            Check(duplicate.eraserUsedThisTurn && !duplicate.eraserRemovalCommitted && duplicate.erasedLetter == "E"
                && _fixture.Network.Refunds(BoosterType.Eraser) == 0, "Cancel A: repeated-word cancellation preserves reversible eraser data and inventory rules");
            _fixture.SaveTo(duplicate, Path.Combine(_root, "restart-duplicate"));

            await NewRound();
            await _fixture.BeginBlink();
            Check(_fixture.Game.GetGameData(false).hasPlacedLetter && _fixture.Load().hasPlacedLetter, "Cancel B: unselected provisional letter is still present during real blinking");
            await Wait(() => _fixture.LastBlink != null && !_fixture.Game.GetGameData(false).hasPlacedLetter, "real blink completion");
            var blink = _fixture.Load();
            Check(!blink.hasPlacedLetter && !blink.boardRows.Contains("Q") && _fixture.Board().SequenceEqual(blink.boardRows), "Cancel B: SelectableLetter.Update completion cancels and saves the provisional letter");
            _fixture.SaveTo(blink, Path.Combine(_root, "restart-blink"));
            long ticks = blink.savedAtUtcTicks;
            EventBus.Raise(_fixture.LastBlink);
            Check(_fixture.Load().savedAtUtcTicks == ticks, "Cancel C: repeated blink completion does not write another checkpoint");

            await NewRound();
            ticks = _fixture.Load().savedAtUtcTicks;
            EventBus.Raise(new CellSelectEvent { letter = _fixture.Cells[5] });
            EventBus.Raise(new CellSelectEvent { letter = _fixture.Cells[5] });
            Check(!((IGameBoosterHost)_fixture.Game).CancelCurrentMove() && _fixture.Load().savedAtUtcTicks == ticks, "Cancel C: visual selection cancellation reports no saved-data change and performs no write");
            await _fixture.AdviceWithoutLetter();
            Check(_fixture.Load().savedAtUtcTicks == ticks, "Cancel C: repeated-word callback without a provisional letter does not save");

            _fixture.Place("Q");
            _fixture.Checkpoint();
            var read = _fixture.Game.GetGameData();
            Check(!read.hasPlacedLetter && !_fixture.Load().hasPlacedLetter && !_fixture.Load().boardRows.Contains("Q"), "Audit: cancellation performed by GetGameData updates recovery without recursion");
            _fixture.SaveTo(_fixture.Load(), Path.Combine(_root, "restart-data-read"));

            await NewRound();
            await _fixture.BeginBlink(letter: "A");
            var acceptingBlink = _fixture.PendingBlinkEvent();
            EventBus.Raise(new LetterBacktrackEvent());
            EventBus.Raise(new LetterSelectEvent { letter = _fixture.Cells[5] });
            EventBus.Raise(new LetterSelectEvent { letter = _fixture.Cells[10] });
            _fixture.Config.Game.delayAIPlaySeconds = 30;
            ticks = _fixture.Load().savedAtUtcTicks;
            EventBus.Raise(new GameGoEvent());
            Check((bool)Get(_fixture.Game, "_isSavingWord"), "Cancel D: real accepted-word animation is in progress");
            EventBus.Raise(acceptingBlink);
            var duringAnimation = _fixture.Game.GetGameData();
            Check(duringAnimation.hasPlacedLetter && _fixture.Load().savedAtUtcTicks == ticks, "Cancel D: late blink and data read cannot cancel or checkpoint a partially accepted word");
            await Wait(() => !_fixture.Game.GetGameData(false).playerTurn, "acceptance after stale blink");
            Check(_fixture.Load().playerWords.Contains("AS") && _fixture.Load().playerScore == 2, "Cancel D: accepted move still commits its history and score after stale callback");

            foreach (GameError error in new[] { GameError.WordAlreadyBeen, GameError.SetLetterNoSelected })
            foreach (string transition in new[] { "turn", "exit", "finish", "new-round" })
            {
                await RepeatedWordRound();
                await _fixture.HoldError(error);
                await _fixture.Transition(transition);
                string[] before = _fixture.Board();
                var checkpoint = _fixture.Recovery.HasRecoverySave ? _fixture.Load() : null;
                await _fixture.ReleaseAdvice();
                Check(_fixture.Board().SequenceEqual(before), "Late advice " + error + " after " + transition + " cannot mutate the current board");
                Check(checkpoint == null ? !_fixture.Recovery.HasRecoverySave : _fixture.Load().savedAtUtcTicks == checkpoint.savedAtUtcTicks,
                    "Late advice " + error + " after " + transition + " cannot save or resurrect recovery");
            }

            foreach (string transition in new[] { "turn", "exit", "finish", "new-round" })
            {
                await NewRound();
                await _fixture.BeginBlink();
                var delayed = _fixture.PendingBlinkEvent();
                await _fixture.Transition(transition);
                if (transition == "new-round") await _fixture.BeginBlink(place: false);
                string[] before = _fixture.Board();
                var checkpoint = _fixture.Recovery.HasRecoverySave ? _fixture.Load() : null;
                EventBus.Raise(delayed);
                Check(_fixture.Board().SequenceEqual(before), "Late completion of a real blink after " + transition + " cannot cancel a new move");
                Check(checkpoint == null ? !_fixture.Recovery.HasRecoverySave : _fixture.Load().savedAtUtcTicks == checkpoint.savedAtUtcTicks,
                    "Late blink after " + transition + " cannot write an obsolete checkpoint");
            }

            await RepeatedWordRound();
            await _fixture.HoldError(GameError.WordAlreadyBeen);
            EventBus.Raise(new GameCancelEvent());
            _fixture.Place("Q");
            _fixture.Checkpoint();
            ticks = _fixture.Load().savedAtUtcTicks;
            await _fixture.ReleaseAdvice();
            Check(_fixture.Board().Contains("Q") && _fixture.Load().savedAtUtcTicks == ticks, "Late advice cannot cancel a replacement move within the same turn");

            await NewRound();
            _fixture.Network.ConsumeGate = new UniTaskCompletionSource();
            var use = _fixture.Use(BoosterType.Mixer);
            await Wait(() => _fixture.Network.ConsumeWaiting, "deferred inventory response");
            await _fixture.Transition("new-round");
            ticks = _fixture.Load().savedAtUtcTicks;
            _fixture.Network.ConsumeGate.TrySetResult();
            await use;
            Check(_fixture.Board().Contains("Q") && _fixture.Load().savedAtUtcTicks == ticks, "Audit: old inventory response cannot run CancelCurrentMove or mixer in a new round");

            await NewRound();
            _fixture.Place("Q");
            _fixture.Checkpoint();
            bool failedBeforeApply = await _fixture.FaultWordSearch(partialApply: false);
            Check(failedBeforeApply && !_fixture.Load().hasPlacedLetter && !_fixture.Load().boardRows.Contains("Q"), "Audit: exception before AI field application saves only the completed cancellation");

            await NewRound();
            _fixture.Place("Q");
            _fixture.Checkpoint();
            ticks = _fixture.Load().savedAtUtcTicks;
            bool failedDuringApply = await _fixture.FaultWordSearch(partialApply: true);
            Check(failedDuringApply && _fixture.Load().savedAtUtcTicks == ticks && _fixture.Load().hasPlacedLetter,
                "Audit: exception during real AI field application cannot replace the last valid checkpoint with a partial board");

            await NewRound();
            _fixture.PrepareLongWordSearch();
            _fixture.Place("Q");
            _fixture.Checkpoint();
            var search = _fixture.Use(BoosterType.Hint);
            await _fixture.CancelActiveSearch();
            await search;
            Check(!_fixture.Load().hasPlacedLetter && !_fixture.Load().boardRows.Contains("Q"), "Audit: cancellation of a real in-flight letter search checkpoints its cancelled provisional move");
            Check(_fixture.Network.TotalRefunds == 0, "Audit: search cancellation preserves existing booster refund rules");
            _fixture.SaveTo(_fixture.Load(), Path.Combine(_root, "restart-cancelled-search"));

            await NewRound();
            _fixture.HoldNextBlock();
            var missing = _fixture.StartMissingWord();
            await _fixture.WaitForBlockedRequest();
            await _fixture.RestartRound(_fixture.LoadFrom(Path.Combine(_root, "restart-duplicate")));
            ticks = _fixture.Load().savedAtUtcTicks;
            _fixture.ReleaseBlock();
            await missing;
            Check(_fixture.Board()[14] == "" && _fixture.Load().savedAtUtcTicks == ticks && _fixture.Network.Refunds(BoosterType.Eraser) == 0,
                "Audit: obsolete missing-word continuation cannot roll back a new round's eraser");
        }

        private static async UniTask SecondSession()
        {
            await NewRound(null, false, "bootstrap");
            var initial = _fixture.LoadFrom(Path.Combine(_root, "restart-initial"));
            await NewRound(initial, true);
            Check(_fixture.Board().SequenceEqual(initial.boardRows) && _fixture.Game.GetGameData(false).playerTurn && _fixture.Network.Consumes == 0, "A: Stop/Play restores initial round without inventory calls");
            var aiTurn = _fixture.LoadFrom(Path.Combine(_root, "restart-ai"));
            await NewRound(aiTurn, true);
            await Wait(() => _fixture.Game.GetGameData(false).playerTurn, "recovered AI turn");
            await UniTask.Delay(1200);
            var afterAi = _fixture.Load();
            Check(_fixture.AiSuccesses == 1 && afterAi.playerTurn && afterAi.opponentWords.Count == 1, "B: Stop/Play recovery of AI ownership executes exactly one real AI move");
            Check(afterAi.opponentScore == afterAi.opponentWords[0].Length && afterAi.playerWords.SequenceEqual(aiTurn.playerWords), "B: completed AI move saves score, history and next player ownership");
            Check(_fixture.Network.Consumes == 0 && _fixture.Network.TotalRefunds == 0, "Recovered AI turn never consumes or refunds boosters");
            await NewRound(afterAi, true);
            var restoredAfterAi = _fixture.Game.GetGameData(false);
            Check(_fixture.Board().SequenceEqual(afterAi.boardRows) && restoredAfterAi.playerTurn && restoredAfterAi.opponentScore == afterAi.opponentScore
                && restoredAfterAi.opponentWords.SequenceEqual(afterAi.opponentWords), "B: restoring completed AI checkpoint preserves board, history, score and player ownership");
            foreach (string slot in new[] { "restart-duplicate", "restart-blink", "restart-data-read", "restart-cancelled-search" })
            {
                var saved = _fixture.LoadFrom(Path.Combine(_root, slot));
                await NewRound(saved, true);
                Check(!_fixture.Game.GetGameData(false).hasPlacedLetter && _fixture.Board().SequenceEqual(saved.boardRows), "Stop/Play: cancelled provisional letter stays absent in " + slot);
                Check(_fixture.Network.Consumes == 0 && _fixture.Network.TotalRefunds == 0, "Stop/Play: " + slot + " restores without replaying inventory operations");
                if (slot == "restart-duplicate")
                    Check(_fixture.Load().eraserUsedThisTurn && !_fixture.Load().eraserRemovalCommitted && _fixture.Load().erasedLetter == "E", "Stop/Play: repeated-word cancellation keeps the original eraser rollback payload");
            }
        }

        private static async UniTask Wait(Func<bool> condition, string label)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(label);
                await UniTask.Yield();
            }
        }

        internal static void Set(object obj, string name, object value)
        {
            for (Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Fields);
                if (field != null) { field.SetValue(obj, value); return; }
            }
            throw new MissingFieldException(obj.GetType().Name, name);
        }

        internal static object Get(object obj, string name)
        {
            for (Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Fields);
                if (field != null) return field.GetValue(obj);
            }
            throw new MissingFieldException(obj.GetType().Name, name);
        }

        private sealed class Fixture
        {
            public readonly GameController Game = new GameController();
            public readonly GameBoosterController Booster = new GameBoosterController();
            public readonly InventoryService Inventory = new InventoryService();
            public readonly FakeInventory Network;
            public readonly RoundAutoSaveService Recovery;
            public readonly ConfigService Config = new ConfigService();
            public readonly TestManualSave Manual;
            public readonly string RecoveryPath;
            public readonly DictionaryService Dictionary = new DictionaryService(null);
            public readonly RecordingAnalytics Analytics = new RecordingAnalytics();
            public AIGameScreen Screen => _screen;
            public List<int> SelectedIndexes => Manager.WordsFieldData.GetSelectedIndexesSnapshot();
            public List<SelectableLetter> Cells => Manager.WordsFieldData.Items;
            private WordsFieldManager Manager => (WordsFieldManager)Get(Game, "_wordsFieldManager");
            private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
            private readonly GameObject _root;
            private readonly TestUI _ui;
            private readonly DiContainer _container = new DiContainer();
            private readonly LanguageDictionaryConfig _language;
            private readonly GameConfig _gameConfig;
            private readonly TestSkins _testSkins;
            private AIGameScreen _screen;
            public int AiSuccesses;
            public ModeBlinkEndEvent LastBlink;

            public Fixture(string directory)
            {
                Directory.CreateDirectory(directory);
                RecoveryPath = Path.Combine(directory, "round_recovery.json");
                Network = new FakeInventory(Inventory);
                foreach (BoosterType type in Enum.GetValues(typeof(BoosterType))) Inventory.SetQuantity(type, 20);
                _gameConfig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Configs/GameConfig.asset"));
                _gameConfig.slowdownDelay = 1;
                _gameConfig.delayAIPlaySeconds = 0.1f;
                _gameConfig.autoApplyDelay = 0.01f;
                Set(Config, "_gameConfig", _gameConfig);
                var localization = new LocalizationService();
                Recovery = new RoundAutoSaveService(Config, localization, directory);
                Manual = new TestManualSave(Game, Path.Combine(directory, "manual.json"));
                var analytics = new AnalyticsService(new List<IAnalyticsProvider> { Analytics }, new AnalyticsPlayerContext());
                var payloads = new GameAnalyticsPayloadFactory();
                var audio = new AudioService(null, Config);
                // Do not use SetSfxVolume: it persists the user's audio preference.
                Set(audio, "_sfxVolume", 0f);
                var skins = _testSkins = new TestSkins();
                _container.BindInstance(audio);
                _container.Bind<ISkinsService>().FromInstance(skins);
                _language = ScriptableObject.CreateInstance<LanguageDictionaryConfig>();
                _language.languageCode = "en";
                _language.alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                _language.consonants = "BCDFGHJKLMNPQRSTVWXYZ";
                Set(Dictionary, "_config", _language);
                Set(Dictionary, "_alphabet", _language.alphabet);
                SetWords(new[] { "STONE", "TONE", "NOTE", "ONE", "ON", "NO", "TO", "AT", "AN", "AS", "SON", "TEN", "NET", "ATE", "EAST", "EAT", "SEAT", "TEAS", "SENT", "TENT", "TEST" });
                _root = new GameObject("Isolated Auto Recovery Fixture");
                _root.SetActive(false);
                _ui = new TestUI(this);
                Add(Game); Add(Booster); Add(Config); Add(Dictionary); Add(localization); Add(analytics); Add(payloads); Add(Network, typeof(InventorySyncService));
                Add(Inventory, typeof(IInventoryService)); Add(Recovery); Add(Manual, typeof(ISaveService)); Add(_ui, typeof(IUIManager));
                Add(new GamePauseService(), typeof(IGamePauseService)); Add(new TestVibration(), typeof(IVibrationService));
                Add(audio); Add(skins, typeof(ISkinsService)); Add(_container);
                Add(new GameAnalyticsReporter(analytics, payloads)); Add(new BoosterAnalyticsReporter(analytics, payloads, Inventory));
                Add(new MissingWordPopupPresenter(_ui, null, new TestLimits(), localization, analytics, Config));
                Add(new TestProfile(), typeof(IProfileService));
                Inject(Game); Inject(Booster);
            }

            private void Add(object obj, Type type = null) => _services[type ?? obj.GetType()] = obj;
            private void Inject(object obj)
            {
                for (Type type = obj.GetType(); type != null; type = type.BaseType)
                    foreach (var field in type.GetFields(Fields))
                        if (field.IsDefined(typeof(InjectAttribute), true) && _services.TryGetValue(field.FieldType, out var service)) field.SetValue(obj, service);
            }

            public T Clone<T>(string path) where T : Component
            {
                var staging = new GameObject("Inactive staging");
                staging.transform.SetParent(_root.transform);
                staging.SetActive(false);
                var clone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), staging.transform);
                foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null) continue;
                    if (component.GetType().Namespace == "UI.SkinBindings") { Object.DestroyImmediate(component); continue; }
                    Inject(component);
                }
                return clone.GetComponent<T>();
            }

            public async UniTask StartAsync(SaveGameData saved, bool recovery)
            {
                _screen = Clone<AIGameScreen>("Assets/UI/Screens/Game/AIGameScreen.prefab");
                _screen.transform.SetParent(_root.transform);
                _root.SetActive(true);
                _screen.gameObject.SetActive(true);
                await UniTask.Yield();
                await Game.InitializeAsync();
                EventBus.Subscribe<OpponentFindWordEvent>(OnAiSuccess);
                EventBus.Subscribe<ModeBlinkEndEvent>(OnBlinkEnd);
                // Normal UI routing resets the screen before raising GameScreenStartEvent.
                _screen.Reset();
                if (saved != null) Game.SetGameData(saved, recovery);
                else
                {
                    // Read preferences without changing them; use their normal difficulty when configured.
                    int difficulty = PlayerPrefs.GetInt(PlayerPrefsKey.ComplexityAI);
                    if (!_gameConfig._сomplexityAIList.Any(x => (int)x.СomplexityAiLevel == difficulty))
                    {
                        var seed = Seed();
                        Game.SetGameData(seed);
                    }
                }
                EventBus.Raise(new GameScreenStartEvent { Screen = _screen, Opponent = GameOpponent.AI, AutoStart = true });
                await UniTask.Yield();
                _screen.TimerBar.StopTimer();
                if (Load() == null) throw new Exception("No initial checkpoint from real GameController startup.");
            }

            private SaveGameData Seed()
            {
                var board = Enumerable.Repeat("", 25).ToArray();
                for (int i = 0; i < 5; i++) board[i + 10] = "STONE"[i].ToString();
                return new SaveGameData { version = _gameConfig.version, savedAtUtcTicks = DateTime.UtcNow.Ticks,
                    localeCode = LocalizationSettings.SelectedLocale.Identifier.Code, mode = "AI", boardSize = 25,
                    boardRows = board, firstWord = "STONE", lastWord = "", playerTurn = true,
                    maxSeconds = 120, levelComplexityAI = (int)ComplexityAI.Normal };
            }

            private void OnAiSuccess(OpponentFindWordEvent data) => AiSuccesses++;
            private void OnBlinkEnd(ModeBlinkEndEvent data) => LastBlink = data;
            public SaveGameData Load() => Recovery.Load();
            public string[] Board() => Manager.WordsFieldData.GetBoardData();
            public void Checkpoint() => ((IGameBoosterHost)Game).SaveRecoveryCheckpoint();
            public void SaveTo(SaveGameData data, string directory) => new RoundAutoSaveService(Config, (LocalizationService)_services[typeof(LocalizationService)], directory).Save(data);
            public SaveGameData LoadFrom(string directory) => new RoundAutoSaveService(Config, (LocalizationService)_services[typeof(LocalizationService)], directory).Load();

            public async UniTask Use(BoosterType type)
            {
                await Booster.HandleUseAsync(new UseBoosterEvent { boosterType = type }, Game);
                await UniTask.Yield();
            }
            public async UniTask ClickBooster(BoosterType type)
            {
                var booster = _screen.BoosterPanel.GetComponentsInChildren<BoosterUI>(true).Single(x => x.Type == type);
                booster.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                await Wait(() => !(bool)Get(Booster, "_boosterProcessing"), "booster button " + type);
                await UniTask.Yield();
            }
            public async UniTask Erase(int index = 10)
            {
                await Use(BoosterType.Eraser);
                EventBus.Raise(new LetterSelectEvent { letter = Cells[index] });
                await UniTask.Yield();
            }

            public async UniTask RestartRound(SaveGameData seed = null)
            {
                _screen.Reset();
                Game.SetGameData(seed ?? Seed());
                EventBus.Raise(new GameScreenStartEvent { Screen = _screen, Opponent = GameOpponent.AI, AutoStart = true });
                await UniTask.Yield();
                _screen.TimerBar.StopTimer();
            }

            private void SubmitRepeatedWord()
            {
                int index = Place("A");
                if (index != 5) throw new Exception("Repeated-word fixture expected cell 5.");
                Checkpoint();
                EventBus.Raise(new LetterSelectEvent { letter = Cells[5] });
                EventBus.Raise(new LetterSelectEvent { letter = Cells[10] });
                EventBus.Raise(new GameGoEvent());
            }
            public async UniTask RepeatedWord()
            {
                SubmitRepeatedWord();
                await Wait(() => _ui.Advice != null && !Game.GetGameData(false).hasPlacedLetter, "repeated-word rejection");
            }
            public async UniTask AdviceWithoutLetter()
            {
                EventBus.Raise(new PlayerErrorEvent { GameError = GameError.WordAlreadyBeen });
                await Wait(() => _ui.Advice != null, "advice without letter");
                await _ui.Advice.WaitForResultAsync();
                await UniTask.Yield();
            }
            public async UniTask HoldError(GameError error)
            {
                _ui.HoldAdvice = true;
                if (error == GameError.WordAlreadyBeen) SubmitRepeatedWord();
                else SubmitUnselectedLetter(place: true);
                await Wait(() => _ui.Advice != null, "held advice");
            }
            public async UniTask ReleaseAdvice()
            {
                ((UnityEngine.UI.Button)Get(_ui.Advice, "_exitButton")).onClick.Invoke();
                await _ui.Advice.WaitForResultAsync();
                await UniTask.Yield();
            }
            private void SubmitUnselectedLetter(bool place, string letter = "Q")
            {
                if (place) Place(letter);
                Checkpoint();
                EventBus.Raise(new LetterSelectEvent { letter = Cells[10] });
                EventBus.Raise(new GameGoEvent());
            }
            public async UniTask BeginBlink(bool place = true, string letter = "Q")
            {
                LastBlink = null;
                SubmitUnselectedLetter(place, letter);
                await Wait(() => Get(Game, "_pendingBlinkLetter") != null, "real blink start");
            }
            public ModeBlinkEndEvent PendingBlinkEvent()
            {
                var letter = (SelectableLetter)Get(Game, "_pendingBlinkLetter");
                return new ModeBlinkEndEvent { letter = letter, moveGeneration = (int)Get(letter, "_blinkMoveGeneration") };
            }
            public async UniTask Transition(string transition)
            {
                Config.Game.delayAIPlaySeconds = 30;
                if (transition == "exit") await Game.ExitRoundAsync();
                else if (transition == "new-round")
                {
                    await RestartRound();
                    Place("Q");
                    Checkpoint();
                }
                else
                {
                    Set(Game, "_maxPasses", transition == "finish" ? (uint)1 : (uint)0);
                    EventBus.Raise(new TimeExpiredEvent());
                    await UniTask.Yield();
                }
            }
            public void HoldNextBlock() => _ui.BlockGate = new UniTaskCompletionSource();
            public void PrepareLongWordSearch()
            {
                SetWords(Enumerable.Range(0, 2000).Select(i => "ZZ" + (char)('A' + i / 676) + (char)('A' + i / 26 % 26) + (char)('A' + i % 26)).ToArray());
            }
            public async UniTask<bool> FaultWordSearch(bool partialApply)
            {
                if (partialApply) _testSkins.ThrowOnSelectedSprite = true;
                else Set(Dictionary, "_wordsByLength", null);
                try
                {
                    await Use(BoosterType.Hint);
                    return false;
                }
                catch (Exception error) when ((partialApply && error is InvalidOperationException) || (!partialApply && error is NullReferenceException))
                {
                    return true;
                }
                finally { _testSkins.ThrowOnSelectedSprite = false; }
            }
            public async UniTask CancelActiveSearch()
            {
                await Wait(() => Get(_screen, "_letterThinkingCts") != null, "actual asynchronous word search");
                _screen.gameObject.SetActive(false); // The real OnDisable path cancels the search token.
            }
            public async UniTask StartMissingWord()
            {
                int index = Place("Q");
                Checkpoint();
                EventBus.Raise(new LetterSelectEvent { letter = Cells[index] });
                EventBus.Raise(new GameGoEvent());
                await Wait(() => _ui.BlockWaiting, "missing-word block wait");
                await _ui.BlockGate.Task;
                await UniTask.Yield();
            }
            public UniTask WaitForBlockedRequest() => Wait(() => _ui.BlockWaiting, "blocked request");
            public void ReleaseBlock() => _ui.BlockGate.TrySetResult();
            public int Place(string letter)
            {
                int index = Enumerable.Range(0, 25).First(i => Manager.TrySetLetter(i));
                EventBus.Raise(new CellSelectEvent { letter = Cells[index] });
                EventBus.Raise(new KeyboardLetterSelectEvent { letter = letter });
                return index;
            }
            public async UniTask MissingWord()
            {
                int count = _ui.MissingWords;
                int index = Place("Q");
                EventBus.Raise(new LetterSelectEvent { letter = Cells[index] });
                EventBus.Raise(new GameGoEvent());
                await Wait(() => _ui.MissingWords > count, "missing word handler");
                await UniTask.Delay(400);
                if (Game.GetGameData(false).hasPlacedLetter) throw new Exception("Missing-word cancellation did not complete.");
            }
            public async UniTask CloseSelection()
            {
                ((UnityEngine.UI.Button)Get(_screen.HoleOverlay, "_closeButton")).onClick.Invoke();
                await UniTask.Delay(400);
            }
            public void OnlyUsedWordInDictionary() => SetWords(new[] { "STONE" });
            private void SetWords(string[] words)
            {
                Set(Dictionary, "_words", new HashSet<string>(words));
                Set(Dictionary, "_wordsByLength", words.GroupBy(x => x.Length).ToDictionary(x => x.Key, x => x.ToList()));
                Set(Dictionary, "_definitionsByWord", words.ToDictionary(x => x, x => "Test definition"));
                Set(Dictionary, "_isLoaded", true);
            }
            public void SelectHintPath()
            {
                // The same search engine supplies a real valid path; only input is automated.
                int placed = Manager.WordsFieldData.PlacedLetterIndex;
                foreach (var word in Dictionary.Words.Where(x => !Manager.WordExist(x)))
                {
                    foreach (int start in Enumerable.Range(0, 25))
                    {
                        var path = new List<int>();
                        if (FindPath(word, 0, start, path) && path.Contains(placed))
                        {
                            _screen.SetTextWord("");
                            foreach (int index in path) EventBus.Raise(new LetterSelectEvent { letter = Cells[index] });
                            return;
                        }
                    }
                }
                throw new Exception("No path including the hint letter.");
            }
            private bool FindPath(string word, int offset, int index, List<int> path)
            {
                if (path.Contains(index) || Cells[index].GetLetter() != word[offset].ToString()) return false;
                path.Add(index);
                if (offset == word.Length - 1) return true;
                foreach (int next in Enumerable.Range(0, 25).Where(i => Math.Abs(i / 5 - index / 5) + Math.Abs(i % 5 - index % 5) == 1))
                    if (FindPath(word, offset + 1, next, path)) return true;
                path.RemoveAt(path.Count - 1);
                return false;
            }
            public async UniTask DisposeAsync()
            {
                EventBus.Unsubscribe<OpponentFindWordEvent>(OnAiSuccess);
                EventBus.Unsubscribe<ModeBlinkEndEvent>(OnBlinkEnd);
                // Simulate loss of the scene without running intentional exit (which deletes recovery).
                Set(Game, "_bStart", false);
                Booster.OnGameFinished();
                Game.Dispose();
                // Real popup fades are fire-and-forget in the game. Let them settle before
                // destroying the test fixture so teardown cannot access destroyed UI targets.
                await UniTask.Delay(700);
                Object.Destroy(_root);
                Object.Destroy(_language);
                Object.Destroy(_gameConfig);
                await UniTask.Yield();
            }

            private sealed class TestUI : IUIManager
            {
                private readonly Fixture _owner;
                public int MissingWords;
                public AdvicePopup Advice;
                public bool HoldAdvice;
                public UniTaskCompletionSource BlockGate;
                public bool BlockWaiting;
                public TestUI(Fixture owner) { _owner = owner; }
                public UniTask<T> ShowScreenAsync<T>(AssetKey key) where T : UIScreen => UniTask.FromResult<T>(null);
                public UniTask<T> ShowScreenAsync<T, TPayload>(AssetKey key, TPayload payload) where T : UIScreen<TPayload> => UniTask.FromResult<T>(null);
                public UniTask<T> HideScreenAsync<T>(AssetKey key) where T : UIScreen => UniTask.FromResult<T>(null);
                public UniTask HideAllScreensAsync() => UniTask.CompletedTask;
                public async UniTask<BlockUIScreen> ShowBlockUIScreenAsync(AssetKey key, BlockUIScreenMode mode)
                {
                    if (BlockGate != null && !BlockWaiting)
                    {
                        BlockWaiting = true;
                        await BlockGate.Task;
                    }
                    return null;
                }
                public UniTask<T> ShowPopupAsync<T>(AssetKey key) where T : UIPopup => throw new NotSupportedException(typeof(T).Name);
                public async UniTask<T> ShowPopupAsync<T, TPayload>(AssetKey key, TPayload payload) where T : UIPopup<TPayload>
                {
                    if (typeof(T) == typeof(AdvicePopup))
                    {
                        var popup = _owner.Clone<AdvicePopup>("Assets/UI/Popups/MessagePopups/AdvicePopup.prefab");
                        popup.transform.SetParent(_owner._root.transform);
                        await popup.PrepareAsync((MessageBoxData)(object)payload);
                        await popup.ShowAsync();
                        Advice = popup;
                        if (!HoldAdvice) CloseAdvice(popup).Forget();
                        return (T)(object)popup;
                    }
                    if (typeof(T) == typeof(MissingWordPopup))
                    {
                        var popup = _owner.Clone<MissingWordPopup>("Assets/UI/Popups/MessagePopups/MissingWordPopup.prefab");
                        popup.transform.SetParent(_owner._root.transform);
                        await popup.PrepareAsync((NewWordWindowEventData)(object)payload);
                        await popup.ShowAsync();
                        MissingWords++;
                        CloseMissing(popup).Forget();
                        return (T)(object)popup;
                    }
                    // Finish cleanup is exercised; keep reward UI awaiting user input in the test.
                    if (typeof(FinishGamePopup).IsAssignableFrom(typeof(T)))
                    {
                        var obj = new GameObject("Held test finish popup", typeof(RectTransform));
                        obj.SetActive(false);
                        obj.transform.SetParent(_owner._root.transform);
                        var popup = obj.AddComponent<T>();
                        Set(popup, "_completionSource", new UniTaskCompletionSource<PopupExitData>());
                        return popup;
                    }
                    throw new NotSupportedException(typeof(T).Name);
                }
                private async UniTaskVoid CloseMissing(MissingWordPopup popup)
                {
                    await UniTask.Yield();
                    ((UnityEngine.UI.Button)Get(popup, "_exitButton")).onClick.Invoke();
                }
                private async UniTaskVoid CloseAdvice(AdvicePopup popup)
                {
                    await UniTask.Yield();
                    ((UnityEngine.UI.Button)Get(popup, "_exitButton")).onClick.Invoke();
                }
                public UniTask HidePopupAsync<T>() where T : UIPopup => UniTask.CompletedTask;
                public UniTask HideAllPopupsAsync() => UniTask.CompletedTask;
            }
        }

        private sealed class RecordingAnalytics : IAnalyticsProvider
        {
            public readonly List<AnalyticsEvent> Events = new List<AnalyticsEvent>();
            public string ProviderName => "Test recorder";
            public bool IsEnabled => true;
            public UniTask InitializeAsync() => UniTask.CompletedTask;
            public void Track(AnalyticsEvent analyticsEvent) => Events.Add(analyticsEvent);
        }

        private sealed class FakeInventory : InventorySyncService
        {
            private readonly InventoryService _inventory;
            private readonly Dictionary<BoosterType, int> _refunds = new Dictionary<BoosterType, int>();
            public int Consumes;
            public bool FailConsume;
            public bool FailRefund;
            public UniTaskCompletionSource ConsumeGate;
            public bool ConsumeWaiting;
            public int TotalRefunds => _refunds.Values.Sum();
            public int Refunds(BoosterType type) => _refunds.TryGetValue(type, out int count) ? count : 0;
            public FakeInventory(InventoryService inventory) { _inventory = inventory; }
            public override async UniTask<bool> TryUseBoosterAsync(BoosterType type)
            {
                await UniTask.Yield();
                if (ConsumeGate != null)
                {
                    ConsumeWaiting = true;
                    await ConsumeGate.Task;
                }
                Consumes++;
                return !FailConsume && _inventory.TryConsumeBooster(type);
            }
            public override async UniTask<bool> GrantBoosterAsync(BoosterType type, int amount)
            {
                await UniTask.Yield();
                _refunds[type] = Refunds(type) + 1;
                if (!FailRefund) _inventory.Add(type, amount);
                return !FailRefund;
            }
        }

        private sealed class TestManualSave : ISaveService
        {
            private readonly GameController _game;
            private readonly string _path;
            public TestManualSave(GameController game, string path) { _game = game; _path = path; }
            public UniTask InitializeAsync() => UniTask.CompletedTask;
            public bool HasSave() => File.Exists(_path);
            public UniTask SaveAsync() { SaveGameStorage.WriteAtomic(_path, _game.GetGameData()); return UniTask.CompletedTask; }
            public UniTask<SaveGameData> LoadAsync() => UniTask.FromResult(HasSave() ? SaveGameStorage.Deserialize(File.ReadAllText(_path)) : null);
            public UniTask ClearAsync() { if (HasSave()) File.Delete(_path); return UniTask.CompletedTask; }
            public void SaveLegacy(SaveGameData data)
            {
                string json = System.Text.RegularExpressions.Regex.Replace(SaveGameStorage.Serialize(data),
                    "\"(?:hasPlacedLetter|placedLetterIndex|eraserUsedThisTurn|eraserRemovalCommitted|erasedCellIndex|erasedLetter|eraserSelectionPending|swapSelectionPending|slowdownRemainingSeconds)\":[^,}]*,?", "");
                File.WriteAllText(_path, json);
            }
        }

        private sealed class TestVibration : IVibrationService
        {
            public bool IsEnabled => false;
            public UniTask InitializeAsync() => UniTask.CompletedTask;
            public void Play(VibrationType type = VibrationType.Light) { }
            public void EnableVibration(bool value) { }
        }
        private sealed class TestLimits : INewWordsLimitsService
        {
            public event Action OnStateChanged { add { } remove { } }
            public WordSubmitAvailability GetAvailability() => new WordSubmitAvailability(true, false, 0, 0);
            public void RegisterSuccessfulSubmit() { }
            public void ResetLimits(bool disableLimits = false) { }
        }
        private sealed class TestProfile : IProfileService
        {
            public string CurrentDisplayName => "Test";
            public UniTask InitializeAsync() => UniTask.CompletedTask;
            public UniTask EnsureDisplayNameAsync() => UniTask.CompletedTask;
            public UniTask<int> GetScoreAsync(bool forceRefresh = false) => UniTask.FromResult(0);
            public UniTask<int> AddScoreAsync(int delta) => UniTask.FromResult(delta);
            public UniTask SetScoreAsync(int score) => UniTask.CompletedTask;
            public UniTask<IReadOnlyList<LeaderboardEntryDto>> GetTopAsync() => UniTask.FromResult<IReadOnlyList<LeaderboardEntryDto>>(new List<LeaderboardEntryDto>());
            public UniTask<RankDto> GetMyRankAsync() => UniTask.FromResult(default(RankDto));
        }
        private sealed class TestSkins : ISkinsService
        {
            public bool ThrowOnSelectedSprite;
            public event Action<SkinData> OnSkinChanged { add { } remove { } }
            public event Action<SkinRuntime> OnRuntimeChanged { add { } remove { } }
            public SkinData SkinCurrent => default;
            public SkinRuntime CurrentRuntime => null;
            public bool SkinRandomSelect => false;
            public SkinsConfig Config => null;
            public UniTask InitializeAsync() => UniTask.CompletedTask;
            public UniTask<bool> ApplySkinAsync(SkinType type, CancellationToken cancellationToken = default) => UniTask.FromResult(true);
            public UniTask<SkinRuntime> EnsureCurrentRuntimeAsync(CancellationToken cancellationToken = default) => UniTask.FromResult<SkinRuntime>(null);
            public Sprite GetSprite(SkinSpriteKey key) => null;
            public GameObject GetPrefab(SkinPrefabKey key) => null;
            public Color GetColor(SkinColorKey key) => Color.white;
            public bool TryGetSprite(SkinSpriteKey key, out Sprite sprite)
            {
                if (ThrowOnSelectedSprite && key == SkinSpriteKey.CellSelectedAlias) throw new InvalidOperationException("Injected failure while applying AI clue highlighting.");
                sprite = null;
                return true;
            }
            public bool TryGetPrefab(SkinPrefabKey key, out GameObject prefab) { prefab = null; return false; }
            public bool TryGetColor(SkinColorKey key, out Color color) { color = Color.white; return true; }
            public void SaveSkinCurrent(SkinType type) { }
            public void TrySaveRandomSelect(bool value) { }
            public void SetSkinRandom() { }
        }
    }
}
#endif
