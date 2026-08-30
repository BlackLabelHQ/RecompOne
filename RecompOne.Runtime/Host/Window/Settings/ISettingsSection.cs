namespace RecompOne.Runtime.Host.Window;

public interface ISettingsSection
{
    string Id { get; }

    string TitleKey { get; }

    int Order { get; }

    void Draw();
}