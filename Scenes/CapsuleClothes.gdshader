shader_type spatial;
render_mode blend_mix, depth_draw_always, depth_prepass_alpha;

uniform vec4 color_hat : source_color = vec4(0.9, 0.8, 0.6, 1.0);
uniform vec4 color_skin : source_color = vec4(0.0, 0.7, 0.9, 1.0);
uniform vec4 color_torso : source_color = vec4(0.9, 0.8, 0.6, 1.0);
uniform vec4 color_legs : source_color = vec4(0.5, 0.5, 0.5, 1.0);
uniform vec4 color_feet : source_color = vec4(0.5, 0.3, 0.2, 1.0);

uniform float band_hat = 0.15;
uniform float band_skin = 0.35;
uniform float band_torso = 0.65;
uniform float band_legs = 0.90;

uniform float sneak_alpha = 1.0;

void fragment() {
    float y = UV.y;
    vec4 col = color_feet;
    
    if (y < band_hat) {
        col = color_hat;
    } else if (y < band_skin) {
        col = color_skin;
    } else if (y < band_torso) {
        col = color_torso;
    } else if (y < band_legs) {
        col = color_legs;
    }
    
    ALBEDO = col.rgb * (0.3 + 0.7 * sneak_alpha);
    ALPHA = sneak_alpha;
}
