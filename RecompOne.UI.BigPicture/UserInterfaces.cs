using RecompOne.Runtime;

namespace RecompOne.UI.BigPicture;

public static class UserInterfaces
{
    public static ImGuiInterface Create()
    {
        Settings.EnsureLoaded();
        return new BigPictureInterface();
    }
}
