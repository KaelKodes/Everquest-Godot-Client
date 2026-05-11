using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainUI
{
	private JsonElement?[] _giveNPCItemData = new JsonElement?[4];

	public void OpenGiveNPCWindow(string npcId, string npcName)
	{
		_giveNPCId = npcId;
		_giveNPCTitle.Text = npcName;
		_giveNPCWindow.Show();
		
		// If holding an item, drop it into slot 1 automatically
		if (_heldItem.HasValue)
		{
			_giveNPCItemData[0] = _heldItem.Value;
			UpdateGiveNPCSlot(0);
			CancelHeldItem();
		}
		SyncInventorySlotsWithGiveNPC();
	}

	private void OnGiveNPCSlotClicked(int index)
	{
		// If there's an item in this slot, return it to cursor
		if (_giveNPCItemData[index].HasValue)
		{
			if (_heldItem.HasValue)
			{
				Log("SYSTEM", "You must empty your cursor first!");
				return;
			}
			
			_heldItem = _giveNPCItemData[index];
			// Give it a generic slot id so it floats
			_heldFromSlotId = -1; 
			_giveNPCItemData[index] = null;
			UpdateGiveNPCSlot(index);
			
			int pIconId = _heldItem.Value.TryGetProperty("icon", out var pProp) ? pProp.GetInt32() : 0;
			var iconMgr = IconManager.Instance;
			_cursorIcon.Texture = (pIconId > 0 && iconMgr != null) ? iconMgr.GetItemIcon(pIconId) : null;
			_cursorIcon.Visible = true;
		}
		else if (_heldItem.HasValue)
		{
			// Drop cursor item into this slot
			_giveNPCItemData[index] = _heldItem.Value;
			UpdateGiveNPCSlot(index);
			CancelHeldItem();
		}
		SyncInventorySlotsWithGiveNPC();
	}

	private void UpdateGiveNPCSlot(int index)
	{
		if (_giveNPCItemData[index].HasValue)
		{
			var item = _giveNPCItemData[index].Value;
			_giveNPCSlots[index].Text = "";
			int iconId = item.TryGetProperty("icon", out var iProp) ? iProp.GetInt32() : 0;
			var iconMgr = IconManager.Instance;
			if (iconId > 0 && iconMgr != null) {
				_giveNPCSlots[index].Icon = iconMgr.GetItemIcon(iconId);
				_giveNPCSlots[index].ExpandIcon = true;
				_giveNPCSlots[index].IconAlignment = HorizontalAlignment.Center;
			}
		}
		else
		{
			_giveNPCSlots[index].Text = (index + 1).ToString();
			_giveNPCSlots[index].Icon = null;
		}
	}

	private void OnGiveNPCOk()
	{
		var items = new List<object>();
		for (int i = 0; i < 4; i++)
		{
			if (_giveNPCItemData[i].HasValue)
			{
				var item = _giveNPCItemData[i].Value;
				var inst_id = item.TryGetProperty("item_id", out var instIdProp) ? instIdProp.GetInt32() : 0;
				var item_id = item.TryGetProperty("eq_item_id", out var itemIdProp) ? itemIdProp.GetInt32() : 0;
				var slotId = item.TryGetProperty("slotId", out var slotProp) ? slotProp.GetInt32() : -1;
				items.Add(new { slot = i + 1, inst_id = inst_id, item_id = item_id, slotId = slotId });
			}
		}

		if (items.Count > 0 && _giveNPCId != null)
		{
			var payload = new
			{
				type = "NPC_GIVE_ITEMS",
				npcId = _giveNPCId,
				items = items
			};
			_client.SendRaw(JsonSerializer.Serialize(payload));
			string who = _giveNPCTitle != null ? _giveNPCTitle.Text.Trim() : "the NPC";
			if (string.IsNullOrEmpty(who)) who = "the NPC";
			string qty = items.Count == 1 ? "your item" : $"{items.Count} items";
			Log("SYSTEM", $"You hand {qty} to {who}.");
		}

		// Clear slots
		for (int i = 0; i < 4; i++)
		{
			_giveNPCItemData[i] = null;
			UpdateGiveNPCSlot(i);
		}
		_giveNPCWindow.Hide();
		SyncInventorySlotsWithGiveNPC();
	}

	private void OnGiveNPCCancel()
	{
		var payload = new { type = "NPC_GIVE_CANCEL" };
		_client.SendRaw(JsonSerializer.Serialize(payload));

		// Clear slots visually
		for (int i = 0; i < 4; i++)
		{
			_giveNPCItemData[i] = null;
			UpdateGiveNPCSlot(i);
		}
		_giveNPCWindow.Hide();
		SyncInventorySlotsWithGiveNPC();
	}

	public JsonElement? GetHeldItem()
	{
		return _heldItem;
	}
}
