namespace Core.Services
{
    public static class AnalyticsEvents
    {
        public static class Startup
        {
            public const string Group = "startup:";
            
            public const string LoadingStarted = Group + "start_loading_started";
            public const string LoadingInternetConnectionRestored = Group + "internet_connection_restored";
            public const string LoadingCompleted = Group + "start_loading_completed";
            public const string PlayFabAuthCompleted = Group + "playfab_auth_completed";
        }
        
        public static class Navigation
        {
            public const string Group = "navigation:";
            
            public const string MainMenuShown = Group + "main_menu_shown";
            public const string PlayMainMenuClicked = Group + "play_main_menu_clicked";
            public const string ContinueMainMenuClicked = Group + "continue_main_menu_clicked";
            public const string SettingsMainMenuClicked = Group + "settings_main_menu_clicked";
            public const string SkinsMainMenuClicked = Group + "skins_main_menu_clicked";
            public const string InfoMainMenuClicked = Group + "info_main_menu_clicked";
            public const string ShopMainMenuClicked = Group + "shop_main_menu_clicked";
            public const string DailyBonusMainMenuClicked = Group + "daily_bonus_main_menu_clicked";
            
            public const string SettingsPopupShown = Group + "settings_popup_shown";
            public const string CloseSettingsClicked = Group + "close_settings_clicked";
            
            public const string OptionsPopupShown = Group + "options_popup_shown";
            public const string CloseOptionsClicked = Group + "close_options_clicked";
            
            public const string LocaleSettingsClicked = Group + "locale_settings_clicked";
            public const string PrivacyPolicySettingsClicked = Group + "privacy_policy_settings_clicked";
            public const string TermsOfServiceSettingsClicked = Group + "terms_of_service_settings_clicked";
            public const string SupportSettingsClicked = Group + "support_settings_clicked";
            
            public const string SkinsPopupShown = Group + "skins_popup_shown";
            public const string CloseSkinsClicked = Group + "close_skins_clicked";
            public const string ApplySkinsClicked = Group + "apply_skins_clicked";
            
            public const string InfoPopupShown = Group + "info_popup_shown";
            public const string CloseInfoClicked = Group + "close_info_clicked";
            
            public const string ShopPopupShown = Group + "shop_popup_shown";
            public const string CloseShopClicked = Group + "close_shop_clicked";
            public const string RewardPopupShown = Group + "reward_popup_shown";
            public const string CloseRewardClicked = Group + "close_reward_clicked";
            public const string OkRewardClicked = Group + "ok_reward_clicked";
            public const string NoAdsPopupShown = Group + "no_ads_popup_shown";
            public const string CloseNoAdsClicked = Group + "close_no_ads_clicked";
            public const string OkNoAdsClicked = Group + "ok_no_ads_clicked";
            public const string GameSetupPopupShown = Group + "game_setup_popup_shown";
            public const string CloseGameSetupClicked = Group + "close_game_setup_clicked";
            public const string BoosterGameSetupClicked = Group + "booster_game_setup_clicked";
            public const string PlayGameSetupClicked = Group + "play_game_setup_clicked";
            public const string SavedGamePopupShown = Group + "saved_game_popup_shown";
            public const string CloseSavedGameClicked = Group + "close_saved_game_clicked";
            public const string RemoveSavedGameClicked = Group + "remove_saved_game_clicked";
            public const string PlaySavedGameClicked = Group + "play_saved_game_clicked";
            public const string BannerLoadingShown = Group + "banner_loading_shown";
            public const string WordInfoPopupShown = Group + "word_info_popup_shown";
            public const string CloseWordInfoClicked = Group + "close_word_info_clicked";
            public const string SendComplaintClicked = Group + "send_complaint_clicked";
            public const string AdvicePopupShown = Group + "advice_popup_shown";
            public const string CloseAdvicePopupClicked = Group + "close_advice_popup_clicked";
            public const string OkAdvicePopupClicked = Group + "ok_advice_popup_clicked";
            public const string MissingWordPopupShown = Group + "missing_word_popup_shown";
            public const string CloseMissingWordPopupClicked = Group + "close_missing_word_popup_clicked";
            public const string NoMissingWordPopupClicked = Group + "no_missing_word_popup_clicked";
            public const string YesMissingWordPopupClicked = Group + "yes_missing_word_popup_clicked";
            public const string ConfirmPassPopupShown = Group + "confirm_pass_popup_shown";
            public const string NoConfirmPassPopupClicked = Group + "no_confirm_pass_popup_clicked";
            public const string YesConfirmPassPopupClicked = Group + "yes_confirm_pass_popup_clicked";
            public const string AiGameExitPopupShown = Group + "ai_game_exit_popup_shown";
            public const string CloseAiGameExitPopupClicked = Group + "close_ai_game_exit_popup_clicked";
            public const string NoAiGameExitPopupClicked = Group + "no_ai_game_exit_popup_clicked";
            public const string YesAiGameExitPopupClicked = Group + "yes_ai_game_exit_popup_clicked";
            public const string FinishGamePopupShown = Group + "finish_game_popup_shown";
            public const string CloseFinishGamePopupClicked = Group + "close_finish_game_popup_clicked";
            public const string OkFinishGamePopupClicked = Group + "ok_finish_game_popup_clicked";
            public const string AdFinishGamePopupClicked = Group + "ad_finish_game_popup_clicked";
            
            public const string AiGameScreenShown = Group + "ai_game_screen_shown";
            public const string AiGameHomeClicked = Group + "ai_game_home_clicked";
            
            public const string StarterBonusPopupShown = Group + "starter_bonus_popup_shown";
            public const string StarterBonusPopupClickedClose = Group + "starter_bonus_popup_clicked_exit";
            public const string StarterBonusPopupClickedGet = Group + "starter_bonus_popup_clicked_get";
            
            public const string DailyBonusPopupShown = Group + "daily_bonus_popup_shown";
            public const string DailyBonusPopupClickedClose = Group + "daily_bonus_popup_clicked_exit";
            public const string DailyBonusPopupClickedTake = Group + "daily_bonus_popup_clicked_take";
        }

        public static class Monetization
        {
            public const string Group = "monetization:";
            
            public const string IapOfferShopClicked = Group + "iap_offer_shop_clicked";
            public const string AdOfferShopClicked = Group + "ad_offer_shop_clicked";
            public const string RemoveAdOfferShopClicked = Group + "remove_ad_offer_shop_clicked";
            public const string PurchaseSuccess = Group + "purchase_success";
            public const string PurchaseFailed = Group + "purchase_failed";
            public const string PurchaseError = Group + "purchase_error";
        }
        
        public static class Ads
        {
            public const string Group = "ads:";
            
            public const string RewardedAvailability = Group + "rewarded_availability";
            public const string RewardedLoadStart = Group + "rewarded_load_start";
            public const string RewardedLoadSuccess = Group + "rewarded_load_success";
            public const string RewardedLoadFailed = Group + "rewarded_load_failed";
            public const string RewardedShowAttempt = Group + "rewarded_show_attempt";
            public const string RewardedShowStart = Group + "rewarded_show_start";
            public const string RewardedShowFailed = Group + "rewarded_show_failed";
            public const string RewardedEarned = Group + "rewarded_earned";
            public const string RewardedClosed = Group + "rewarded_closed";

            public const string InterstitialLoadStart = Group + "interstitial_load_start";
            public const string InterstitialLoadSuccess = Group + "interstitial_load_success";
            public const string InterstitialLoadFailed = Group + "interstitial_load_failed";
            public const string InterstitialShowAttempt = Group + "interstitial_show_attempt";
            public const string InterstitialShowStart = Group + "interstitial_show_start";
            public const string InterstitialShowFailed = Group + "interstitial_show_failed";
            public const string InterstitialClosed = Group + "interstitial_closed";
        }

        public static class GameFlow
        {
            public const string Group = "game_flow:";
            
            public const string AiGameStarted = Group + "ai_game_started";
            public const string AiSavedGameStarted = Group + "ai_saved_game_started";
            public const string PauseGameClicked = Group + "pause_game_clicked";
            public const string TimeExpired = Group + "time_expired";
            public const string PassGameClicked = Group + "pass_game_clicked";
            public const string HistoryGameClicked = Group + "history_game_clicked";
            public const string CloseHistoryGameClicked = Group + "close_history_game_clicked";
            public const string WordInfoClicked = Group + "word_info_clicked";
            public const string CellSelected = Group + "cell_selected";
            public const string CellUnselected = Group + "cell_unselected";
            public const string KeyboardLetterClicked = Group + "keyboard_letter_clicked";
            public const string LetterPutSuccess = Group + "letter_put_success";
            public const string CancelClicked = Group + "cancel_clicked";
            public const string ApplyWordClicked = Group + "apply_word_clicked";
            public const string FinishGame = Group + "finish_game";
            public const string ReplayClicked = Group + "replay_clicked";
            public const string AiMoveStart = Group + "ai_move_start";
            public const string AiMoveSuccess = Group + "ai_move_success";
            public const string AiMoveFail = Group + "ai_move_fail";
        }

        public static class BoosterUsage
        {
            public const string Group = "booster_usage:";

            public const string BoosterGameClicked = Group + "booster_game_clicked";
            public const string LetterBoosterSuccess = Group + "letter_booster_success";
            public const string LetterBoosterFail = Group + "letter_booster_fail";
            public const string HintBoosterSuccess = Group + "hint_booster_success";
            public const string HintBoosterFail = Group + "hint_booster_fail";
            public const string HintBoosterResult = Group + "hint_booster_result";
            public const string EraserBoosterShown = Group + "eraser_booster_shown";
            public const string SwapBoosterShown = Group + "swap_booster_shown";
            public const string EraserBoosterClosed = Group + "eraser_booster_closed";
            public const string SwapBoosterClosed = Group + "swap_booster_closed";
            public const string EraserBoosterSuccess = Group + "eraser_booster_success";
            public const string SwapBoosterSuccess = Group + "swap_booster_success";
            public const string SlowdownBoosterSuccess = Group + "slowdown_booster_success";
            public const string SlowdownBoosterEnd = Group + "slowdown_booster_end";
            public const string MixerBoosterSuccess = Group + "mixer_booster_success";
            public const string MixerBoosterFail = Group + "mixer_booster_fail";
        }
        
        public static class Parameter
        {
            public const string WaitTimeMs = "wait_time_ms";
            public const string WaitTimeSeconds = "wait_time_seconds";
            public const string PlayFabId = "playfab_id";
            public const string NewUser = "new_user";
            public const string Banner = "banner";
            public const string Locale = "locale";
            public const string Skin = "skin";
            public const string SkinRandom = "skin_random";
            public const string Sound = "sound";
            public const string Vibration = "vibration";
            public const string Giro = "giro";
            public const string Source = "source";
            public const string ProductId = "product_id";
            public const string Reward = "reward";
            public const string Price = "price";
            public const string Day = "day";
            public const string Jackpot = "jackpot";
            public const string LimitRemain = "limit_remain";
            public const string Result = "result";
            public const string Reason = "reason";
            public const string ReportLimit = "report_limit";
            public const string RewardType = "reward_type";
            public const string Placement = "placement";
            public const string IsReady = "is_ready";
            public const string IsLoading = "is_loading";
            public const string Cooldown = "cooldown";
            public const string DailyLimitReached = "daily_limit_reached";
            public const string LoadTimeMs = "load_time_ms";
            public const string LoadReason = "load_reason";
            public const string Error = "error";
            public const string WasRewarded = "was_rewarded";
            public const string ComplexityAi = "complexity_ai";
            public const string DurationRound = "duration_round";
            public const string Boosters = "boosters";
            public const string BoosterClicked = "booster_clicked";
            public const string StartWord = "start_word";
            public const string Word = "word";
            public const string WordLength = "word_length";
            public const string Letter = "letter";
            public const string Index = "index";
            public const string DurationRoundLeft = "duration_round_left";
            public const string CellsEmpty = "cells_empty";
            public const string Score = "score";
            public const string ScoreOpponent = "score_opponent";
            public const string Pass = "pass";
            public const string PassOpponent = "pass_opponent";
            public const string WhoseMove = "whose_move";
            public const string State = "state";
            public const string SavedGameRemoved = "saved_game_removed";
            public const string Field = "field";
            public const string FieldBefore = "field_before";
            public const string FieldAfter = "field_after";
            public const string EraseItem = "erase_item";
            public const string SlowdownDelay = "slowdown_delay";
            public const string MixerPattern = "mixer_pattern";
            public const string MixerArranger = "mixer_arranger";
            public const string TargetIndexes = "target_indexes";
            public const string DontShow = "dont_show";
            public const string WinsInSeriesCount = "wins_in_series_count";
            public const string WinsInSeriesMax = "wins_in_series_max";
            public const string FinishAdOffer = "finish_ad_offer";
        }
        
        public static class Option
        {
            public const string On = "on";
            public const string Off = "off";
            public const string Success = "success";
            public const string NotReady = "not_ready";
            public const string Cooldown = "cooldown";
            public const string Limit = "limit";
            public const string NoFill = "no_fill";
            public const string Timeout = "timeout";
            public const string Network = "network";
            public const string InternalError = "internal_error";
            public const string InvalidRequest = "invalid_request";
            public const string Disabled = "disabled";
            public const string TooEarly = "too_early";
            public const string Unknown = "unknown";
            public const string Initial = "initial";
            public const string ReloadOnDemand = "reload_on_demand";
            public const string ReloadAfterClose = "reload_after_close";
            public const string ReloadAfterFail = "reload_after_fail";
            public const string Owner = "owner";
            public const string Opponent = "opponent";
            public const string Win = "win";
            public const string Lose = "lose";
            public const string Draft = "draft";
            public const string Chest = "chest";
            public const string Booster = "booster";
        }

        public static class Placement
        {
            public const string RepeatGame = "repeat_game";
            public const string ExitGame = "exit_game";
            public const string Global = "global";
        }
    }
}
