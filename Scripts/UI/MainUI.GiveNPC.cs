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
			
			_cursorLabel.Text = _heldItem.Value.TryGetProperty("itemName", out var n) ? n.GetString() : "Item";
			_cursorLabel.Visible = true;
		}
		else if (_heldItem.HasValue)
		{
			// Drop cursor item into this slot
			_giveNPCItemData[index] = _heldItem.Value;
			UpdateGiveNPCSlot(index);
			CancelHeldItem();
		}
	}

	private void UpdateGiveNPCSlot(int index)
	{
		if (_giveNPCItemData[index].HasValue)
		{
			var item = _giveNPCItemData[index].Value;
			string name = item.TryGetProperty("itemName", out var n) ? n.GetString() : "Item";
			_giveNPCSlots[index].Text = name.Length > 8 ? name.Substring(0, 8) + ".." : name;
		}
		else
		{
			_giveNPCSlots[index].Text = (index + 1).ToString();
		}
	}

	private void OnGiveNPCOk()
	{
		var items = new List<object>();
		for (int i = 0; i < 4; i++)
		{
			if (_giveNPCItemData[i].HasValue)
			{
				var inst_id = _giveNPCItemData[i].Value.TryGetProperty("item_id", out var instIdProp) ? instIdProp.GetInt32() : 0;
				var item_id = _giveNPCItemData[i].Value.TryGetProperty("eq_item_id", out var itemIdProp) ? itemIdProp.GetInt32() : 0; 
				items.Add(new { slot = i + 1, inst_id = inst_id, item_id = item_id });
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
			Log("SYSTEM", "You handed the items to the NPC.");
		}

		// Clear slots
		for (int i = 0; i < 4; i++)
		{
			_giveNPCItemData[i] = null;
			UpdateGiveNPCSlot(i);
		}
		_giveNPCWindow.Hide();
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
	}

	public JsonElement? GetHeldItem()
	{
		return _heldItem;
	}
}
