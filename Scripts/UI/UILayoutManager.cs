using Godot;
using Godot.Collections;

public static class UILayoutManager
{
    private static Dictionary _currentLayoutData = new Dictionary();

    public static string GetCurrentLayoutFileName(string characterName)
    {
        var size = DisplayServer.WindowGetSize();
        string res = $"{size.X}x{size.Y}";
        
        if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Windowed)
        {
            res = "Windowed";
        }
        
        // Ensure directory exists
        if (!DirAccess.DirExistsAbsolute("user://ui_layouts"))
        {
            DirAccess.MakeDirAbsolute("user://ui_layouts");
        }
        
        return $"user://ui_layouts/UI_{characterName}_{GameState.ServerName}_{res}.json";
    }

    public static void Initialize(string characterName)
    {
        string path = GetCurrentLayoutFileName(characterName);
        if (FileAccess.FileExists(path))
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file != null)
            {
                var json = new Json();
                var err = json.Parse(file.GetAsText());
                if (err == Error.Ok && json.Data.VariantType == Variant.Type.Dictionary)
                {
                    _currentLayoutData = json.Data.AsGodotDictionary();
                }
            }
        }
        else
        {
            _currentLayoutData = new Dictionary();
        }
    }

    public static Dictionary GetSection(string sectionName)
    {
        if (_currentLayoutData.TryGetValue(sectionName, out Variant val) && val.VariantType == Variant.Type.Dictionary)
        {
            return val.AsGodotDictionary();
        }
        return new Dictionary();
    }

    public static void SetSection(string sectionName, Dictionary data)
    {
        _currentLayoutData[sectionName] = data;
    }

    public static void SaveLayout(string characterName)
    {
        string path = GetCurrentLayoutFileName(characterName);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file != null)
        {
            file.StoreString(Json.Stringify(_currentLayoutData, "\t"));
        }
    }

    public static Array<string> GetAvailableLayouts()
    {
        var list = new Array<string>();
        if (!DirAccess.DirExistsAbsolute("user://ui_layouts")) return list;
        
        using var dir = DirAccess.Open("user://ui_layouts");
        if (dir != null)
        {
            dir.ListDirBegin();
            string fileName = dir.GetNext();
            while (fileName != "")
            {
                if (!dir.CurrentIsDir() && fileName.StartsWith("UI_") && fileName.EndsWith(".json"))
                {
                    list.Add(fileName);
                }
                fileName = dir.GetNext();
            }
        }
        return list;
    }

    // Helper for loading specific external layouts for the Copy tool
    public static Dictionary LoadLayoutFromFile(string fileName)
    {
        string path = $"user://ui_layouts/{fileName}";
        if (!FileAccess.FileExists(path)) return new Dictionary();
        
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) return new Dictionary();
        
        var json = new Json();
        var err = json.Parse(file.GetAsText());
        if (err == Error.Ok && json.Data.VariantType == Variant.Type.Dictionary)
        {
            return json.Data.AsGodotDictionary();
        }
        return new Dictionary();
    }
}
