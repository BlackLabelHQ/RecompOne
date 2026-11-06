using System.Numerics;

namespace RecompOne.UI;

public static class ShadersPage
{
    private static string? _repository;
    private static string _branch = "";
    
    public static void Register()
    {
        SettingsPages.Page(SettingsPages.Shaders, "menu.shaders", 15).Add("repository", Draw);
    }
    
    private static void Draw()
    {
        if (_repository == null)
        {
            _repository = ShaderManager.RepositoryUrl;
            _branch = ShaderManager.RepositoryBranch;
        }
        
        ImGui.SetNextItemWidth(320f);
        ImGui.InputText(Localization.T("shaders.repository"), ref _repository, 512);
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();
        SettingsWidgets.Hint("shaders.repository_hint");
        
        ImGui.SetNextItemWidth(320f);
        ImGui.InputTextWithHint(Localization.T("shaders.branch"), "HEAD", ref _branch, 128);
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();
        SettingsWidgets.Hint("shaders.branch_hint");
        
        if (!ShaderRepository.TryParse(_repository, out _, out _))
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Localization.T("shaders.bad_repository"));
        
        if (ImGui.Button(Localization.T("shaders.default_repository")))
        {
            _repository = ShaderManager.DefaultRepository;
            _branch = "";
            Save();
        }
        
        ImGui.Spacing();
        if (ImGui.Button(FontSet.With(FontSet.FolderOpen, Localization.T("shaders.open_folder"))))
            ShaderManager.OpenFolder();
    }
    
    private static void Save()
    {
        _repository = _repository!.Trim();
        _branch = _branch.Trim();
        ShaderManager.ConfigureRepository(_repository, _branch);
    }
}
