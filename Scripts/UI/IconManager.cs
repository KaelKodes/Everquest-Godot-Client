using Godot;
using System.Collections.Generic;
using System.IO;

public partial class IconManager : Node
{
	public static IconManager Instance { get; private set; }
	
	private Dictionary<int, Texture2D> _sheetCache = new Dictionary<int, Texture2D>();
	private Dictionary<int, AtlasTexture> _iconCache = new Dictionary<int, AtlasTexture>();

	public override void _Ready()
	{
		Instance = this;
		ClientLocalSettings.Load();
	}

	/// <summary>Clears cached sheets/atlas slices after the user changes the dragitem folder in Options.</summary>
	public void InvalidateItemIconCaches()
	{
		_sheetCache.Clear();
		_iconCache.Clear();
	}

	private static string NormalizeFsPath(string path) => path.Replace('\\', '/');

	private static Texture2D LoadTextureFromAbsolutePath(string absolutePath)
	{
		string p = NormalizeFsPath(absolutePath);
		if (!Godot.FileAccess.FileExists(p)) return null;
		var image = new Image();
		if (image.Load(p) != Error.Ok) return null;
		return ImageTexture.CreateFromImage(image);
	}

	private Texture2D LoadDragItemSheet(int sheetIndex)
	{
		if (_sheetCache.TryGetValue(sheetIndex, out var cached))
			return cached;

		Texture2D sheetTex = null;
		string folder = ClientLocalSettings.DragItemIconsFolder;
		if (!string.IsNullOrWhiteSpace(folder))
		{
			string basePath = folder.Trim().TrimEnd('/', '\\');
			foreach (var ext in new[] { ".dds", ".tga" })
			{
				string combined = NormalizeFsPath(Path.Combine(basePath, $"dragitem{sheetIndex}{ext}"));
				sheetTex = LoadTextureFromAbsolutePath(combined);
				if (sheetTex != null) break;
			}
		}

		if (sheetTex == null)
		{
			string pathDds = $"res://Assets/UI/ClassicUI/dragitem{sheetIndex}.dds";
			string pathTga = $"res://Assets/UI/ClassicUI/dragitem{sheetIndex}.tga";
			if (ResourceLoader.Exists(pathDds))
				sheetTex = GD.Load<Texture2D>(pathDds);
			else if (ResourceLoader.Exists(pathTga))
				sheetTex = GD.Load<Texture2D>(pathTga);
		}

		_sheetCache[sheetIndex] = sheetTex;
		return sheetTex;
	}

	/// <summary>
	/// Returns a 40x40 AtlasTexture for the specified classic EQ icon ID.
	/// Automatically slices the correct dragitemXXX.dds sprite sheet.
	/// </summary>
	public AtlasTexture GetItemIcon(int iconId)
	{
		// In EQ, item icons typically start at 500, so an icon ID of 500 maps to dragitem1.dds index 0.
		// Sometimes data sets normalize this to 0. We'll handle raw values.
		if (iconId >= 500)
			iconId -= 500;
			
		if (iconId < 0) return null;

		if (_iconCache.TryGetValue(iconId, out var cached))
			return cached;

		int sheetIndex = (iconId / 36) + 1;
		int localIndex = iconId % 36;

		var sheetTex = LoadDragItemSheet(sheetIndex);
		if (sheetTex == null) return null;

		// EQ sprite sheets are packed vertically (top-to-bottom, then left-to-right)
		int col = localIndex / 6;
		int row = localIndex % 6;

		var atlas = new AtlasTexture();
		atlas.Atlas = sheetTex;
		atlas.Region = new Rect2(col * 40, row * 40, 40, 40);

		_iconCache[iconId] = atlas;
		return atlas;
	}
	
	public AtlasTexture GetSpellGem(int memIcon)
	{
		if (memIcon < 0) return null;

        // Titanium memIcon offset starts at 2001
        int adjustedIcon = memIcon >= 2001 ? memIcon - 2001 : memIcon;

		int cacheKey = -2000 - memIcon; 
		if (_iconCache.TryGetValue(cacheKey, out var cached))
			return cached;

		// Titanium UI sheets are 100 icons each for gemicons (10x10)
		int sheetIndex = (adjustedIcon / 100) + 1;
		int localIndex = adjustedIcon % 100;
		
		string sheetStr = sheetIndex.ToString("D2"); // 01, 02
		string path = $"res://Assets/UI/ClassicUI/gemicons{sheetStr}.tga";
		
		int sheetKey = -2000 - sheetIndex;
		if (_sheetCache.TryGetValue(sheetKey, out var sheetTex))
		{
            // Valid cache
		}
        else
		{
			if (ResourceLoader.Exists(path))
				sheetTex = GD.Load<Texture2D>(path);
			else
            {
                _sheetCache[sheetKey] = null;
				return null;
            }
				
			_sheetCache[sheetKey] = sheetTex;
		}

		if (sheetTex == null) return null;

		int col = localIndex % 10;
		int row = localIndex / 10;

		var atlas = new AtlasTexture();
		atlas.Atlas = sheetTex;
		atlas.Region = new Rect2(col * 24, row * 24, 24, 24);

		_iconCache[cacheKey] = atlas;
		return atlas;
	}

	/// <summary>
	/// Returns a 40x40 AtlasTexture for the specified classic EQ spell icon (used in spellbook).
	/// Slices from spellsXX.tga (6x6 grid, 36 icons per sheet).
	/// </summary>
	public AtlasTexture GetSpellIcon(int iconId)
	{
		if (iconId < 0) return null;

		int cacheKey = -3000 - iconId; 
		if (_iconCache.TryGetValue(cacheKey, out var cached))
			return cached;

		// RoF2 icon offset usually starts at 2497
		int adjustedIcon = iconId >= 2497 ? iconId - 2497 : iconId;

		int sheetIndex = (adjustedIcon / 36) + 1;
		int localIndex = adjustedIcon % 36;
		
		string sheetStr = sheetIndex.ToString("D2");
		string path = $"res://Assets/UI/ClassicUI/spells{sheetStr}.tga";
		
		int sheetKey = -3000 - sheetIndex;
		if (_sheetCache.TryGetValue(sheetKey, out var sheetTex))
		{
            // Valid cache
		}
        else
		{
			if (ResourceLoader.Exists(path))
				sheetTex = GD.Load<Texture2D>(path);
			else
            {
                _sheetCache[sheetKey] = null;
				return null;
            }
				
			_sheetCache[sheetKey] = sheetTex;
		}

		if (sheetTex == null) return null;

		// EQ sprite sheets are packed vertically (top-to-bottom, then left-to-right)
		int col = localIndex / 6;
		int row = localIndex % 6;

		var atlas = new AtlasTexture();
		atlas.Atlas = sheetTex;
		atlas.Region = new Rect2(col * 40, row * 40, 40, 40);

		_iconCache[cacheKey] = atlas;
		return atlas;
	}
}
