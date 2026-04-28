using Godot;

public partial class VisionManager : Node
{
	private WorldEnvironment _environment;
	private CanvasModulate _canvasModulate;

	public override void _Ready()
	{
		_environment = GetParent().GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		
		// Ensure we have a CanvasModulate for screen tinting
		_canvasModulate = GetParent().GetNodeOrNull<CanvasModulate>("VisionTint");
		if (_canvasModulate == null)
		{
			_canvasModulate = new CanvasModulate();
			_canvasModulate.Name = "VisionTint";
			GetParent().AddChild(_canvasModulate);
		}
	}

	public void SetVisionStyle(string styleName)
	{
		if (_canvasModulate == null) return;

		// Default no tint
		_canvasModulate.Color = Colors.White;
		
		if (_environment != null && _environment.Environment != null)
		{
			_environment.Environment.AdjustmentEnabled = true;
			_environment.Environment.AdjustmentBrightness = 1.0f;
			_environment.Environment.AdjustmentContrast = 1.0f;
			_environment.Environment.AdjustmentSaturation = 1.0f;
		}

		switch (styleName)
		{
			case "normal_weak":
				// Weaker vision in direct sunlight ( handled server-side mostly, but client could add bloom )
				break;
			case "infravision":
				// Red/orange heat signatures
				_canvasModulate.Color = new Color(1.4f, 0.9f, 0.7f); 
				if (_environment != null && _environment.Environment != null)
				{
					_environment.Environment.AdjustmentBrightness = 1.3f;
				}
				break;
			case "ultravision":
				// Ultra-enhanced purple darkvision (turns night to day with slight tint)
				_canvasModulate.Color = new Color(1.1f, 1.0f, 1.3f); // Slight purple tint
				if (_environment != null && _environment.Environment != null)
				{
					_environment.Environment.AdjustmentBrightness = 1.5f; // Boost overall exposure
					_environment.Environment.AdjustmentContrast = 1.1f;   // Slight contrast boost
					_environment.Environment.AdjustmentSaturation = 1.1f; 
				}
				break;
			case "cateye":
				// Light night vision with green/grey tint
				_canvasModulate.Color = new Color(0.6f, 0.9f, 0.6f);
				if (_environment != null && _environment.Environment != null)
				{
					_environment.Environment.AdjustmentSaturation = 0.5f;
					_environment.Environment.AdjustmentBrightness = 1.3f;
				}
				break;
			case "serpentsight":
				// Weaker infra/ultra, clear underwater
				_canvasModulate.Color = new Color(0.8f, 1.0f, 0.8f);
				if (_environment != null && _environment.Environment != null)
				{
					_environment.Environment.AdjustmentBrightness = 1.1f;
					// Note: Water fog clearing logic would be handled by telling the water shader/fog volume
				}
				break;
			case "starlight":
				// Elven silver-blue
				_canvasModulate.Color = new Color(0.8f, 0.9f, 1.1f);
				if (_environment != null && _environment.Environment != null)
				{
					_environment.Environment.AdjustmentBrightness = 1.1f;
				}
				break;
			case "nocturnal":
				// Tapetum lucidum, desaturated
				if (_environment != null && _environment.Environment != null)
				{
					_environment.Environment.AdjustmentSaturation = 0.3f;
					_environment.Environment.AdjustmentBrightness = 1.4f;
				}
				break;
			case "darksight":
				// Sepia/amber
				_canvasModulate.Color = new Color(1.1f, 0.9f, 0.7f);
				break;
			case "predator":
				// Muddy red-orange
				_canvasModulate.Color = new Color(1.1f, 0.6f, 0.4f);
				if (_environment != null && _environment.Environment != null)
				{
					_environment.Environment.AdjustmentContrast = 1.2f;
					_environment.Environment.AdjustmentSaturation = 1.1f;
				}
				break;
			default:
				// normal
				break;
		}
	}
}
