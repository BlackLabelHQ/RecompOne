namespace RecompOne.UI;

public static class ShadersPage
{
    private static string? _repository;
    private static string _branch = "";
    
    public static void Register()
    {
        SettingsPages.Page(SettingsPages.Shaders, "menu.shaders", 15).WithIcon(FontSet.Wand).Add("repository", Draw);
    }
    
    private static void Draw()
    {
        if (_repository == null)
        {
            _repository = ShaderManager.RepositoryUrl;
            _branch = ShaderManager.RepositoryBranch;
        }
        
        ImGui.SetNextItemWidth(320f);
        if (Ui.Input(Localization.T("shaders.repository"), ref _repository, 512, null,
                Localization.T("shaders.repository_hint")))
            Save();
        
        ImGui.SetNextItemWidth(320f);
        if (Ui.Input(Localization.T("shaders.branch"), ref _branch, 128, "HEAD", Localization.T("shaders.branch_hint")))
            Save();
        
        if (!ShaderRepository.TryParse(_repository, out _, out _))
            Ui.Note(Localization.T("shaders.bad_repository"));
        
        if (Ui.Button(Localization.T("shaders.default_repository")))
        {
            _repository = ShaderManager.DefaultRepository;
            _branch = "";
            Save();
        }
        
        if (Ui.Button(FontSet.With(FontSet.FolderOpen, Localization.T("shaders.open_folder"))))
            ShaderManager.OpenFolder();
    }
    
    private static void Save()
    {
        _repository = _repository!.Trim();
        _branch = _branch.Trim();
        ShaderManager.ConfigureRepository(_repository, _branch);
    }
}
