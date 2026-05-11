using Godot;
using System.Text.Json;

/// <summary>Read-only list of another player&apos;s inventory for GM inspection.</summary>
public partial class GmInventoryPeekWindow : Window
{
	private ItemList _list;

	public override void _Ready()
	{
		Title = "GM: Inventory";
		Size = new Vector2I(420, 520);
		Exclusive = false;
		Unresizable = false;
		CloseRequested += () => Hide();

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 8);
		margin.AddThemeConstantOverride("margin_right", 8);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		AddChild(margin);

		_list = new ItemList();
		_list.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		margin.AddChild(_list);
	}

	public void ShowForPlayer(string playerName, JsonElement inventoryArray)
	{
		Title = $"GM: {playerName}";
		_list.Clear();
		var iconMgr = IconManager.Instance;
		foreach (var item in inventoryArray.EnumerateArray())
		{
			int slotId = item.TryGetProperty("slotId", out var s) ? s.GetInt32() : -1;
			string nm = item.TryGetProperty("itemName", out var n) ? n.GetString() : "?";
			int qty = item.TryGetProperty("quantity", out var q) ? q.GetInt32() : 1;
			int eq = item.TryGetProperty("equipped", out var e) ? e.GetInt32() : 0;
			int iconId = item.TryGetProperty("icon", out var ic) ? ic.GetInt32() : 0;
			string line = eq == 1 ? $"[{slotId}] {nm} x{qty} (equipped)" : $"[{slotId}] {nm} x{qty}";
			_list.AddItem(line);
			if (iconId > 0 && iconMgr != null)
			{
				var tex = iconMgr.GetItemIcon(iconId);
				if (tex != null)
					_list.SetItemIcon(_list.ItemCount - 1, tex);
			}
		}
		if (_list.ItemCount == 0)
			_list.AddItem("(empty)");
		Show();
	}
}
