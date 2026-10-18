using Core.Events;
using Core.Services;
using Game.Logic.Mixer;
using Core.Services.Inventory;

namespace Game.Logic
{
    public sealed class BoosterAnalyticsReporter
    {
        private readonly AnalyticsService _analytics;
        private readonly GameAnalyticsPayloadFactory _payloadFactory;
        private readonly IInventoryService _inventory;

        public BoosterAnalyticsReporter(
            AnalyticsService analytics,
            GameAnalyticsPayloadFactory payloadFactory,
            IInventoryService inventory)
        {
            _analytics = analytics;
            _payloadFactory = payloadFactory;
            _inventory = inventory;
        }

        public void TrackLetterBoosterSuccess(IGameBoosterHost host, string word, string[] boardData)
        {
            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.LetterBoosterSuccess,
                _payloadFactory.CreateLetterBoosterSuccessPayload(
                    word,
                    host.LocaleCode,
                    boardData));
        }

        public void TrackLetterBoosterFail(IGameBoosterHost host, float currentTimerValue, string[] boardData)
        {
            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.LetterBoosterFail,
                _payloadFactory.CreateLetterBoosterFailPayload(
                    host.LocaleCode,
                    host.RoundDurationSeconds,
                    currentTimerValue,
                    boardData));
        }

        public void TrackHintBoosterSuccess(IGameBoosterHost host, string word, string[] boardData)
        {
            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.HintBoosterSuccess,
                _payloadFactory.CreateLetterBoosterSuccessPayload(word, host.LocaleCode, boardData));
        }

        public void TrackHintBoosterFail(IGameBoosterHost host, float currentTimerValue, string[] boardData)
        {
            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.HintBoosterFail,
                _payloadFactory.CreateLetterBoosterFailPayload(host.LocaleCode, host.RoundDurationSeconds, currentTimerValue, boardData));
        }

        public void TrackEraserBoosterShown(IGameBoosterHost host, string[] boardData)
        {
            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.EraserBoosterShown,
                _payloadFactory.CreateEraserBoosterShownPayload(
                    host.LocaleCode,
                    boardData));
        }
        
        public void TrackSwapBoosterShown(IGameBoosterHost host, string[] boardData)
        {
            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.SwapBoosterShown,
                _payloadFactory.CreateSwapBoosterShownPayload(
                    host.LocaleCode,
                    boardData));
        }

        public void TrackEraserBoosterClosed(IGameBoosterHost host, float currentTimerValue)
        {
            if (host == null)
                return;

            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.EraserBoosterClosed,
                _payloadFactory.CreateEraserBoosterClosedPayload(
                    host.RoundDurationSeconds,
                    currentTimerValue,
                    _inventory.Boosters));
        }
        
        public void TrackSwapBoosterClosed(IGameBoosterHost host, float currentTimerValue)
        {
            if (host == null)
                return;

            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.SwapBoosterClosed,
                _payloadFactory.CreateSwapBoosterClosedPayload(
                    host.RoundDurationSeconds,
                    currentTimerValue,
                    _inventory.Boosters));
        }

        public void TrackEraserBoosterSuccess(IGameBoosterHost host, CellSelectSuccessEvent eventData,
            float currentTimerValue, string[] boardData)
        {
            if (host == null || eventData == null || !eventData.isEraserSuccess || eventData.letter == null)
                return;

            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.EraserBoosterSuccess,
                _payloadFactory.CreateEraserBoosterSuccessPayload(
                    host.LocaleCode,
                    boardData,
                    eventData.letter.Index,
                    eventData.erasedLetter,
                    host.RoundDurationSeconds,
                    currentTimerValue,
                    _inventory.Boosters));
        }

        public void TrackSwapBoosterSuccess(IGameBoosterHost host, CellSelectSuccessEvent eventData,
            float currentTimerValue)
        {
            if (host == null || eventData == null || !eventData.isSwapSuccess)
                return;

            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.SwapBoosterSuccess,
                _payloadFactory.CreateSwapBoosterSuccessPayload(
                    host.LocaleCode,
                    host.RoundDurationSeconds,
                    currentTimerValue,
                    eventData.boardBeforeSwap,
                    eventData.boardAfterSwap,
                    eventData.swapFirstIndex,
                    eventData.swapSecondIndex,
                    _inventory.Boosters));
        }

        public void TrackSlowdownBoosterSuccess(IGameBoosterHost host, int slowdownDelay, float currentTimerValue,
            string[] boardData)
        {
            if (host == null)
                return;

            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.SlowdownBoosterSuccess,
                _payloadFactory.CreateSlowdownBoosterSuccessPayload(
                    slowdownDelay,
                    host.LocaleCode,
                    host.RoundDurationSeconds,
                    currentTimerValue,
                    boardData,
                    _inventory.Boosters));
        }

        public void TrackSlowdownBoosterEnd()
        {
            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.SlowdownBoosterEnd);
        }

        public void TrackMixerBoosterSuccess(IGameBoosterHost host, string[] boardBefore, MixerResult result,
            float currentTimerValue)
        {
            if (host == null || result == null)
                return;

            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.MixerBoosterSuccess,
                _payloadFactory.CreateMixerBoosterSuccessPayload(
                    host.LocaleCode,
                    host.RoundDurationSeconds,
                    currentTimerValue,
                    boardBefore,
                    result.BoardData,
                    result.PatternId,
                    result.ArrangerId,
                    result.TargetIndexes,
                    _inventory.Boosters));
        }

        public void TrackMixerBoosterFail(IGameBoosterHost host, string[] boardData, string reason,
            float currentTimerValue)
        {
            if (host == null)
                return;

            _analytics.TrackEvent(AnalyticsEvents.BoosterUsage.MixerBoosterFail,
                _payloadFactory.CreateMixerBoosterFailPayload(
                    host.LocaleCode,
                    host.RoundDurationSeconds,
                    currentTimerValue,
                    boardData,
                    reason,
                    _inventory.Boosters));
        }
    }
}
