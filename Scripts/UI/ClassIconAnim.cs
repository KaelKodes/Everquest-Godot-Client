using Godot;
using System;

/// <summary>
/// Animates a sprite sheet applied to a TextureRect using an AtlasTexture.
/// Because Godot 4 removed AnimatedTexture, this handles the classic EQ class animations.
/// </summary>
public partial class ClassIconAnim : TextureRect
{
	[Export] public int HFrames { get; set; } = 4;
	[Export] public int VFrames { get; set; } = 2;
	[Export] public float Fps { get; set; } = 6.0f;
	[Export] public bool PingPong { get; set; } = true;
	[Export] public int ActiveFrames { get; set; } = 5; // e.g. 5 frames out of the sheet
	[Export] public float LoopWaitTime { get; set; } = 5.0f;

	private AtlasTexture _atlas;
	private float _timer = 0f;
	private float _waitTimer = 0f;
	private int _currentFrame = 0;
	private int _direction = 1;

	public override void _Ready()
	{
		if (Texture is AtlasTexture existingAtlas) {
			_atlas = existingAtlas;
		} else if (Texture != null) {
			var newAtlas = new AtlasTexture();
			newAtlas.Atlas = Texture;
			Texture = newAtlas;
			_atlas = newAtlas;
		}
		UpdateRegion();
	}

	public override void _Process(double delta)
	{
		if (_atlas == null || _atlas.Atlas == null || HFrames <= 0 || VFrames <= 0)
			return;

		_timer += (float)delta;

		if (_waitTimer > 0f)
		{
			_waitTimer -= (float)delta;
			if (_waitTimer <= 0f)
			{
				_waitTimer = 0f;
				_timer = 0f; // Reset timer so we don't skip frames after waiting
			}
			return;
		}

		float frameDuration = 1.0f / Fps;

		if (_timer >= frameDuration)
		{
			_timer -= frameDuration;
			
			// We limit to ActiveFrames if it's set, otherwise max frames
			int limit = (ActiveFrames > 0 && ActiveFrames <= HFrames * VFrames) ? ActiveFrames : (HFrames * VFrames);
			if (limit <= 1) return;

			if (PingPong)
			{
				_currentFrame += _direction;
				if (_currentFrame >= limit)
				{
					_currentFrame = limit - 2;
					_direction = -1;
					// Handle edge case of 2 frames
					if (_currentFrame < 0) _currentFrame = 0;
				}
				else if (_currentFrame < 0)
				{
					_direction = 1;
					if (LoopWaitTime > 0)
					{
						_currentFrame = 0;
						_waitTimer = LoopWaitTime;
					}
					else
					{
						_currentFrame = 1;
					}
				}
			}
			else
			{
				_currentFrame++;
				if (_currentFrame >= limit)
				{
					_currentFrame = 0;
					if (LoopWaitTime > 0)
					{
						_waitTimer = LoopWaitTime;
					}
				}
			}
			UpdateRegion();
		}
	}

	public void SetTextureSheet(Texture2D tex)
	{
		if (_atlas != null && _atlas.Atlas == tex)
			return; // Don't interrupt animation if it's the exact same texture!

		if (_atlas == null)
		{
			_atlas = new AtlasTexture();
			Texture = _atlas;
		}
		_atlas.Atlas = tex;
		_currentFrame = 0;
		_direction = 1;
		_waitTimer = 0f;
		_timer = 0f;
		UpdateRegion();
	}

	private void UpdateRegion()
	{
		if (_atlas == null || _atlas.Atlas == null || HFrames <= 0 || VFrames <= 0)
			return;

		int frameWidth = _atlas.Atlas.GetWidth() / HFrames;
		int frameHeight = _atlas.Atlas.GetHeight() / VFrames;

		int col = _currentFrame % HFrames;
		int row = _currentFrame / HFrames;

		_atlas.Region = new Rect2(col * frameWidth, row * frameHeight, frameWidth, frameHeight);
	}
}
