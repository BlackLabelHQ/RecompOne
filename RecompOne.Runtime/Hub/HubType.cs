namespace RecompOne.Runtime.Hub;

public sealed record HubType(string Id, string LabelKey, Func<string> Directory, Action? Installed = null);
