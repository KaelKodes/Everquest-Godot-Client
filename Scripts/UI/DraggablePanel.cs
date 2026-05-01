using Godot;
using System;

public partial class DraggablePanel : Control
{
	private bool _dragging = false;

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed)
				{
					_dragging = true;
					// Bring window to the front
					GetParent().MoveChild(this, -1);
				}
				else
				{
					_dragging = false;
					if (MainUI.Instance != null) {
						MainUI.Instance.SaveWindowPositions();
					}
				}
			}
		}
		else if (@event is InputEventMouseMotion mm)
		{
			if (_dragging)
			{
				Position += mm.Relative;
			}
		}
	}
}
