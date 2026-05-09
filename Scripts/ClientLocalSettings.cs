using Godot;

/// <summary>Client-only preferences stored under <c>user://</c> (not character or server specific).</summary>
public static class ClientLocalSettings
{
	private const string CfgPath = "user://client_settings.cfg";
	private const string Section = "ui";

	/// <summary>Absolute path to the folder containing <c>dragitem1.dds</c> … <c>dragitem178.dds</c> (e.g. EverQuest <c>uifiles/default</c>). Empty = use built-in <c>res://Assets/UI/ClassicUI/</c>.</summary>
	public static string DragItemIconsFolder { get; private set; } = "";

	public static void Load()
	{
		var cf = new ConfigFile();
		if (cf.Load(CfgPath) != Error.Ok)
		{
			DragItemIconsFolder = "";
			return;
		}
		DragItemIconsFolder = cf.GetValue(Section, "dragitem_icons_folder", "").AsString();
	}

	public static void SaveDragItemIconsFolder(string folder)
	{
		DragItemIconsFolder = (folder ?? "").Trim();
		var cf = new ConfigFile();
		cf.Load(CfgPath);
		cf.SetValue(Section, "dragitem_icons_folder", DragItemIconsFolder);
		cf.Save(CfgPath);
	}
}
