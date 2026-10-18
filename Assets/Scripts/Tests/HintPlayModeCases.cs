#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Core.Config;
using Core.Data;
using Core.Events;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.AutoRecovery
{
    public static partial class AutoRecoveryPlayModeSuite
    {
        private static async UniTask HintRound()
        {
            await NewRound();
            _fixture.HintWords("STONE", "STOP", "POTS", "TOP", "STOPS");
            _fixture.Config.Game.delayAIPlaySeconds = 30;
            await _fixture.Use(BoosterType.Hint);
        }

        private static async UniTask WaitHintReveal()
        {
            await Wait(() => _fixture.Screen.IsHintRevealActive, "Hint reveal starts");
        }

        private static async UniTask TapReveal()
        {
            var button = (Button)Get(_fixture.Screen, "_hintRevealBlock");
            button.onClick.Invoke();
            button.onClick.Invoke();
            await Wait(() => !_fixture.Screen.IsHintRevealActive && !(bool)Get(_fixture.Game, "_isSavingWord")
                && !(bool)Get(_fixture.Game, "_isResolvingHint"), "Hint reveal completes once");
        }

        private static async UniTask HintLifecycleCases()
        {
            await NewRound();
            _fixture.PrepareLongWordSearch();
            var searching = _fixture.Use(BoosterType.Hint);
            await Wait(() => Get(_fixture.Screen, "_letterThinkingCts") != null, "Hint asynchronous search");
            Check(!(bool)Get(_fixture.Screen.TimerBar, "_bRun"), "Hint 2: timer is stopped during search");
            await searching;
            Check(!_fixture.Booster.IsHintActive && !_fixture.Load().hasPlacedLetter && _fixture.Network.Refunds(BoosterType.Hint) == 1,
                "Hint 2: failed search leaves no state/letter and refunds once");

            await HintRound();
            var hint = _fixture.Booster.ActiveHint.Copy();
            Check(hint.targetWord == "STOP" && hint.targetLength == 4 && hint.IsValidForBoard(_fixture.Board()),
                "Hint 2: longest available target and ordered path are retained");
            Check((bool)Get(_fixture.Screen.TimerBar, "_bRun") && _fixture.Screen.GetTextWord() == ""
                && _fixture.SelectedIndexes.Count == 0 && _fixture.Load().hint.targetLength == 4,
                "Hint 2: timer resumes; target remains hidden and persisted");
            int consumed = _fixture.Network.Consumes;
            await _fixture.Use(BoosterType.Hint);
            await _fixture.Use(BoosterType.Letter);
            EventBus.Raise(new KeyboardLetterSelectEvent { letter = "Z" });
            Check(_fixture.Network.Consumes == consumed && _fixture.Board()[hint.cellIndex] == hint.letter,
                "Hint 2: repeated Hint, Letter and keyboard cannot replace/add its provisional letter");
            _fixture.Screen.TimerBar.SetCurrentValue(17);
            await _fixture.Manual.SaveAsync();
            var manual = await _fixture.Manual.LoadAsync();
            Check(manual.hint.targetWord == hint.targetWord && manual.currentSeconds == 17 && manual.hasPlacedLetter,
                "Hint 2: real manual snapshot preserves Hint instead of cancelling it");
            _fixture.SaveTo(manual, Path.Combine(_root, "restart-hint-pending"));
            await NewRound(manual);
            Check(_fixture.Booster.IsHintActive && _fixture.Booster.ActiveHint.IsValidForBoard(_fixture.Board())
                && (bool)Get(_fixture.Cells[hint.cellIndex], "_isClue") && _fixture.Network.Consumes == 0 && _fixture.Network.TotalRefunds == 0,
                "Hint 2: manual load restores target/path/highlight with no inventory calls");

            // Exact target and a different maximum are both accepted without reveal.
            foreach (string word in new[] { "STOP", "POTS" })
            {
                await HintRound();
                _fixture.SelectWord(word);
                EventBus.Raise(new GameGoEvent());
                await Wait(() => !_fixture.Game.GetGameData(false).playerTurn, "maximum accepted");
                Check(!_fixture.Screen.IsHintRevealActive && !_fixture.Booster.IsHintActive
                    && _fixture.Load().playerScore == 4 && _fixture.Load().playerWords.SequenceEqual(new[] { word }),
                    "Hint 2: maximum " + word + " commits normally without reveal");
            }

            await HintRound();
            _fixture.SelectWord("TOP");
            EventBus.Raise(new GameGoEvent());
            await WaitHintReveal();
            var committed = _fixture.Load();
            string[] committedBoard = _fixture.Board();
            Check(committed.playerScore == 3 && committed.playerWords.SequenceEqual(new[] { "TOP" }) && !committed.playerTurn
                && committed.hint == null && _fixture.AiSuccesses == 0 && !(bool)Get(_fixture.Screen.TimerBar, "_bRun"),
                "Hint 2: shorter word is committed canonically before reveal, AI and timer remain stopped");
            // Wait for the actual sequential animation; inspect reveal's per-cell tracking order.
            await UniTask.Delay(400);
            Check(_fixture.Cells.Count(x => Get(x, "_highlightState").ToString() == "Highlighted") == 4,
                "Hint 2: reveal sequentially highlights the complete stored target path");
            Check(_fixture.Board().SequenceEqual(committedBoard) && _fixture.Game.GetGameData(false).playerScore == 3
                && !_fixture.Game.GetGameData(false).playerWords.Contains("STOP"),
                "Hint 2: reveal never changes board, score or history");
            _fixture.SaveTo(committed, Path.Combine(_root, "restart-hint-committed"));
            await TapReveal();
            Check(!_fixture.Booster.IsHintActive && _fixture.SelectedIndexes.Count == 0
                && _fixture.Cells.All(x => Get(x, "_highlightState").ToString() == "None"),
                "Hint 2: double tap cleans reveal without duplicate acceptance");

            await HintRound();
            hint = _fixture.Booster.ActiveHint.Copy();
            _fixture.SelectWord("POT"); // A valid path with the clue, absent from this test dictionary.
            EventBus.Raise(new GameGoEvent());
            await Wait(() => _fixture.MissingWordCount == 1, "Hint missing-word flow");
            await UniTask.Delay(400);
            Check(_fixture.Booster.IsHintActive && _fixture.Board()[hint.cellIndex] == hint.letter
                && _fixture.Booster.ActiveHint.targetWord == hint.targetWord && _fixture.Load().hint != null
                && !_fixture.Screen.IsHintRevealActive && _fixture.Network.TotalRefunds == 0,
                "Hint 2: dictionary rejection retains target/path/letter without refund or reveal");
            // Ordinary validation error must also preserve it (no selected clue).
            EventBus.Raise(new GameGoEvent());
            await UniTask.Delay(500);
            Check(_fixture.Booster.IsHintActive && _fixture.Game.GetGameData(false).hasPlacedLetter
                && (bool)Get(_fixture.Cells[hint.cellIndex], "_isClue"), "Hint 2: empty selection error keeps the clue active");
            _fixture.SaveTo(_fixture.Load(), Path.Combine(_root, "restart-hint-invalid"));
            EventBus.Raise(new GameCancelEvent());
            Check(!_fixture.Booster.IsHintActive && _fixture.Board()[hint.cellIndex] == "" && _fixture.Network.TotalRefunds == 0,
                "Hint 2: explicit Cancel removes clue and state without refund");
            _fixture.Place("Q");
            Check(_fixture.Game.GetGameData(false).hasPlacedLetter && _fixture.Board().Contains("Q"),
                "Hint 2: ordinary manual placement works after Cancel");

            foreach (bool timeout in new[] { false, true })
            {
                await HintRound();
                hint = _fixture.Booster.ActiveHint.Copy();
                if (timeout) _fixture.Screen.TimerBar.SetCurrentValue(_fixture.Game.GetGameData(false).maxSeconds);
                if (timeout) EventBus.Raise(new TimeExpiredEvent());
                else EventBus.Raise(new GameSkipEvent());
                await WaitHintReveal();
                Check(_fixture.Load().hint.passPending && _fixture.Load().hint.timedOut == timeout
                    && _fixture.Board()[hint.cellIndex] == hint.letter && _fixture.Game.GetGameData(false).playerPasses == 0,
                    "Hint 2: " + (timeout ? "timeout" : "Skip") + " reveals before removing provisional letter or committing pass");
                if (timeout) _fixture.SaveTo(_fixture.Load(), Path.Combine(_root, "restart-hint-timeout"));
                // Competing callbacks during reveal cannot resolve the turn a second time.
                EventBus.Raise(new TimeExpiredEvent()); EventBus.Raise(new GameSkipEvent()); EventBus.Raise(new GameCancelEvent());
                await TapReveal();
                Check(!_fixture.Load().playerTurn && _fixture.Load().playerPasses == 1 && _fixture.Board()[hint.cellIndex] == ""
                    && _fixture.Load().hint == null && _fixture.Network.TotalRefunds == 0,
                    "Hint 2: " + (timeout ? "timeout" : "Skip") + " resolves exactly one pass and clears clue without refund");
            }

            await HintRound();
            _fixture.Config.Game.delayAIPlaySeconds = 0.01f;
            _fixture.SelectWord("TOP");
            EventBus.Raise(new GameGoEvent());
            await WaitHintReveal();
            Check(_fixture.AiSuccesses == 0, "Hint 2: AI is never started before reveal completes");
            await TapReveal();
            await Wait(() => _fixture.AiSuccesses == 1, "one real AI move after reveal");
            await UniTask.Delay(900);
            Check(_fixture.AiSuccesses == 1 && _fixture.Game.GetGameData(false).playerWords.Count == 1,
                "Hint 2: real AI runs once after repeated reveal taps");

            await HintRound();
            _fixture.SelectWord("TOP"); EventBus.Raise(new GameGoEvent());
            await WaitHintReveal();
            await _fixture.RestartRound();
            await UniTask.Delay(400);
            Check(!_fixture.Booster.IsHintActive && !_fixture.Screen.IsHintRevealActive && _fixture.Load().playerScore == 0
                && _fixture.Load().playerTurn && _fixture.AiSuccesses == 0,
                "Hint 2: new round cancels reveal; stale callbacks cannot change board/score or start AI");

            // A middle insertion must export C-A-T rather than A-C-T.
            var custom = _fixture.Load();
            custom.boardRows = Enumerable.Repeat("", 25).ToArray(); custom.boardRows[10] = "C"; custom.boardRows[12] = "T";
            await NewRound(custom);
            _fixture.HintWords("CAT"); await _fixture.Use(BoosterType.Hint);
            Check(_fixture.Booster.ActiveHint.cellIndex == 11 && _fixture.Booster.ActiveHint.orderedPath.SequenceEqual(new[] { 10, 11, 12 }),
                "Hint 2: middle insertion produces the actual ordered reading path");

            custom = _fixture.Load();
            custom.hint = null; custom.hasPlacedLetter = false;
            // Snake-shaped 25-letter word with its middle letter missing.
            string longest = "ABCDEFGHIJKLMNOPQRSTUVWXY";
            int[] snake = Enumerable.Range(0, 25).Select(i => i / 5 * 5 + (i / 5 % 2 == 0 ? i % 5 : 4 - i % 5)).ToArray();
            custom.boardRows = Enumerable.Repeat("", 25).ToArray();
            for (int i = 0; i < 25; i++) if (i != 12) custom.boardRows[snake[i]] = longest[i].ToString();
            await NewRound(custom);
            _fixture.HintWords(longest, "AB");
            _fixture.Config.Game.boosterLetterAiSettings.WordLength.Max = 2;
            await _fixture.Use(BoosterType.Hint);
            Check(_fixture.Booster.ActiveHint.targetLength == 25 && _fixture.Booster.ActiveHint.IsValidForBoard(_fixture.Board())
                && _fixture.Booster.ActiveHint.orderedPath.SequenceEqual(snake),
                "Hint 2: search reaches the 25-cell maximum independently of Letter/AI settings");

            await HintRound();
            await _fixture.Use(BoosterType.Slowdown);
            EventBus.Raise(new TimeExpiredEvent());
            await WaitHintReveal();
            Check(!_fixture.Booster.IsSlowdownActive && _fixture.Load().hint.passPending,
                "Hint 2: timeout with active Slowdown checkpoints a valid pending pass");
            await TapReveal();

            await HintRound();
            _fixture.Screen.TimerBar.SetCurrentValue(23);
            await _fixture.Game.ExitRoundAsync(saveManually: true);
            var exited = await _fixture.Manual.LoadAsync();
            Check(exited.hint != null && exited.hasPlacedLetter && exited.currentSeconds == 23 && !_fixture.Booster.IsHintActive,
                "Hint 2: intentional save-and-exit preserves Hint snapshot and clears live state");
        }

        private static async UniTask HintAfterRestartCases()
        {
            foreach (string slot in new[] { "restart-hint-pending", "restart-hint-invalid" })
            {
                var saved = _fixture.LoadFrom(Path.Combine(_root, slot));
                await NewRound(saved, true);
                Check(_fixture.Booster.IsHintActive && _fixture.Booster.ActiveHint.orderedPath.SequenceEqual(saved.hint.orderedPath)
                    && _fixture.Booster.ActiveHint.IsValidForBoard(_fixture.Board()) && _fixture.Network.Consumes == 0 && _fixture.Network.TotalRefunds == 0,
                    "Hint 2 Stop/Play: " + slot + " restores target/path without search or inventory operations");
            }
            var committed = _fixture.LoadFrom(Path.Combine(_root, "restart-hint-committed"));
            await NewRound(committed, true);
            await Wait(() => _fixture.AiSuccesses == 1, "recovered committed Hint move");
            Check(_fixture.Game.GetGameData(false).playerScore == 3 && _fixture.Game.GetGameData(false).playerWords.SequenceEqual(new[] { "TOP" })
                && !_fixture.Booster.IsHintActive && _fixture.Network.Consumes == 0,
                "Hint 2 Stop/Play: committed short word never repeats score/history or Hint consume");
            var timedOut = _fixture.LoadFrom(Path.Combine(_root, "restart-hint-timeout"));
            await NewRound(timedOut, true);
            Check(!_fixture.Booster.IsHintActive && !_fixture.Screen.IsHintRevealActive && _fixture.Game.GetGameData(false).playerPasses == 1
                && !_fixture.Game.GetGameData(false).hasPlacedLetter && _fixture.Network.Consumes == 0 && _fixture.Network.TotalRefunds == 0,
                "Hint 2 Stop/Play: interrupted timeout reveal commits its pending pass once without replaying animation");
        }
    }
}
#endif
