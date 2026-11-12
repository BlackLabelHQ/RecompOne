using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace RecompOne.Runtime.Host;

public sealed record HostContext(GL Gl, IWindow Window, IInputContext Input);

public interface IUserInterface
{
    bool BlocksStartup { get; }
    bool BlocksExit { get; }
    
    void Load(HostContext host);
    void BeginFrame(double delta);
    void Draw();
    void EndFrame();
    void Close();
    
    void ShowNotice(string message);
    
    bool PickingDisc => false;
    
    void PickDisc(string missing)
    {
    }
    void ReportCrash(Exception error);
    
    void BeginModLoading(int total);
    void UpdateModLoading(int current, string name);
    void EndModLoading();
}
