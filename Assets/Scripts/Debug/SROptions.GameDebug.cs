using System.ComponentModel;
using Core.Config;
using Core.DebugTools;

public partial class SROptions
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Category("Dev/Inventory")]
    [DisplayName("Выдать 5 × Буковка")]
    public void GrantLetterBoosters() => SRDebugGameSettingsBridge.Instance?.GrantLetterBoosters();

    [Category("Dev/Inventory")]
    [DisplayName("Выдать 5 × Подсказка")]
    public void GrantHintBoosters() => SRDebugGameSettingsBridge.Instance?.GrantHintBoosters();
#endif

    [Category("Dev/Game")]
    [DisplayName("Настройка времени хода"), NumberRange(GameConfig.MinDurationGameSeconds, int.MaxValue), Increment(1), Sort(1)]
    public int DurationGameSeconds
    {
        get
        {
            if (SRDebugGameSettingsBridge.Instance != null)
                return SRDebugGameSettingsBridge.Instance.DurationGameSeconds;

            return GameDurationSettings.GetDurationGameSeconds(null);
        }
        set
        {
            if (SRDebugGameSettingsBridge.Instance != null)
                SRDebugGameSettingsBridge.Instance.DurationGameSeconds = value;
            else
                GameDurationSettings.SetDurationGameSeconds(value);
        }
    }
    
    [Category("Dev/Game")]
    [DisplayName("Включить автоматическую победу"), Sort(2)]
    public bool IsAutoWin
    {
        get
        {
            return GameDebug.IsAutoWin;
        }
        set
        {
            GameDebug.IsAutoWin = value;
        }
    }
}
