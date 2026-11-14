namespace RecompOne.UI.BigPicture.Tabs;

public readonly record struct ShellPage(string Id, string Title, string Icon, Action Draw, Action? Enter = null);
