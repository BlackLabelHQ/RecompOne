namespace RecompOne.UI.BigPicture.Components;

public static class Labels
{
    private const string IdMarker = "##";
    
    public static string Visible(string label)
    {
        var id = label.IndexOf(IdMarker, StringComparison.Ordinal);
        return id < 0 ? label : label[..id];
    }
}
