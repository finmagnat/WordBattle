using System.ComponentModel;
using Core.DebugTools;
using Core.Services;
using Core.Services.ReportWord;
using UnityEngine.Localization.Settings;
using Zenject;

public partial class SROptions
{   
    [Category("Dev/Words/Common")]
    [DisplayName("Language"), Sort(1)]
    public DebugLanguage Language
    {
        get => DebugLanguageCode.SelectedLanguage;
        set => DebugLanguageCode.SelectedLanguage = value;
    }
    
    [Category("Dev/Words/Common")]
    [DisplayName("Set Localization UI"), Sort(2)]
    public void SetLocalizationUI()
    {
        SRDebugNewWordsBridge.Instance?.SetLocalization();
    }

    //------------ NEW WORDS

    [Category("Dev/Words/New")]
    [DisplayName("New Word To Add"), Sort(10)]
    public string NewWordToAdd
    {
        get => SRDebugNewWordsBridge.Instance != null
            ? SRDebugNewWordsBridge.Instance.WordToAdd
            : string.Empty;
        set
        {
            if (SRDebugNewWordsBridge.Instance == null) return;
            SRDebugNewWordsBridge.Instance.WordToAdd = value;
        }
    }

    [Category("Dev/Words/New")]
    [DisplayName("Add Pending Word"), Sort(11)]
    public void AddPendingWord()
    {
        SRDebugNewWordsBridge.Instance?.AddPendingWord();
    }

    [Category("Dev/Words/New")]
    [DisplayName("Get Pending Words To Clipboard"), Sort(12)]
    public void GetPendingWordsToClipboard()
    {
        SRDebugNewWordsBridge.Instance?.DumpPendingWordsToClipboard();
    }

    [Category("Dev/Words/New")]
    [DisplayName("Clear Pending Words"), Sort(13)]
    public void ClearPendingWords()
    {
        SRDebugNewWordsBridge.Instance?.ClearAllPendingWords();
    }

    [Category("Dev/Words/New")]
    [DisplayName("Reset New Words Limits"), Sort(14)]
    public void ResetNewWordsLimits()
    {
        SRDebugNewWordsBridge.Instance?.ResetNewWordsLimits();
    }
    
    [Category("Dev/Words/New")]
    [DisplayName("Disable New Words Limits"), Sort(14)]
    public void DisableNewWordsLimits()
    {
        SRDebugNewWordsBridge.Instance?.ResetNewWordsLimits(true);
    }

    //------------ REPORT WORDS

    [Category("Dev/Words/Report")]
    [DisplayName("Report Word To Add"), Sort(20)]
    public string ReportWordToAdd
    {
        get => SRDebugReportWordBridge.Instance != null
            ? SRDebugReportWordBridge.Instance.WordToAdd
            : string.Empty;
        set
        {
            if (SRDebugReportWordBridge.Instance == null) return;
            SRDebugReportWordBridge.Instance.WordToAdd = value;
        }
    }

    [Category("Dev/Words/Report")]
    [DisplayName("Report Reason"), Sort(21)]
    public ReportReason ReportReason
    {
        get => SRDebugReportWordBridge.Instance != null
            ? SRDebugReportWordBridge.Instance.Reason
            : ReportReason.None;
        set
        {
            if (SRDebugReportWordBridge.Instance == null) return;
            SRDebugReportWordBridge.Instance.Reason = value;
        }
    }

    [Category("Dev/Words/Report")]
    [DisplayName("Add Report Word"), Sort(22)]
    public void AddReportWord()
    {
        SRDebugReportWordBridge.Instance?.AddReportWord();
    }

    [Category("Dev/Words/Report")]
    [DisplayName("Get Report Words To Clipboard"), Sort(23)]
    public void GetReportWordsToClipboard()
    {
        SRDebugReportWordBridge.Instance?.DumpReportWordsToClipboard();
    }

    [Category("Dev/Words/Report")]
    [DisplayName("Clear Report Words"), Sort(24)]
    public void ClearReportWords()
    {
        SRDebugReportWordBridge.Instance?.ClearAllReportWords();
    }

    [Category("Dev/Words/Report")]
    [DisplayName("Reset Report Word Limits"), Sort(25)]
    public void ResetReportWordLimits()
    {
        SRDebugReportWordBridge.Instance?.ResetReportWordLimits();
    }
    
    [Category("Dev/Words/Report")]
    [DisplayName("Disable Report Word Limits"), Sort(14)]
    public void DisableReportWordsLimits()
    {
        SRDebugNewWordsBridge.Instance?.ResetNewWordsLimits(true);
    }
}