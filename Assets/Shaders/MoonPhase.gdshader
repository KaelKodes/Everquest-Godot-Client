shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled;

uniform sampler2D moon_texture : source_color, filter_linear_mipmap;
uniform float phase : hint_range(0.0, 1.0) = 0.5;

void fragment() {
    vec4 tex_color = texture(moon_texture, UV);
    
    // Default transparent if it's black background (from generated image)
    if (tex_color.r < 0.1 && tex_color.g < 0.1 && tex_color.b < 0.1 && tex_color.a > 0.0) {
        ALPHA = 0.0;
    } else {
        ALBEDO = tex_color.rgb;
        ALPHA = tex_color.a;
        
        // Calculate the shadow curve for the phase
        // UV.x goes from 0.0 (left) to 1.0 (right)
        // phase 0.0 = New Moon (all black)
        // phase 0.5 = Full Moon (all white)
        // phase 1.0 = New Moon (all black)
        
        float mapped_x = UV.x * 2.0 - 1.0; // -1 to 1
        float mapped_y = UV.y * 2.0 - 1.0; // -1 to 1
        
        // Distance from center
        float dist = sqrt(mapped_x*mapped_x + mapped_y*mapped_y);
        
        if (dist <= 1.0) {
            // It's inside the moon circle
            // We cast a shadow across the sphere
            // Phase cycle:
            // 0.0 -> 0.5: Waxing (shadow moves right to left)
            // 0.5 -> 1.0: Waning (shadow moves right to left)
            
            float shadow_x = 0.0;
            bool is_shadow = false;
            
            if (phase < 0.5) {
                // Waxing (0.0 to 0.5) - Right side is lit first
                float progress = phase * 2.0; // 0.0 to 1.0
                // progress 0.0 = fully shadowed
                // progress 0.5 = left half shadowed (first quarter)
                // progress 1.0 = full moon
                
                // Shadow covers from -1.0 up to a curve
                float curve_x = cos(progress * 3.14159); // 1.0 down to -1.0
                
                if (progress < 0.5) {
                    // Crescent (shadow is convex on right)
                    if (mapped_x < 0.0) is_shadow = true;
                    else if (mapped_x < curve_x * sqrt(1.0 - mapped_y*mapped_y)) is_shadow = true;
                } else {
                    // Gibbous (shadow is concave on left)
                    if (mapped_x < 0.0 && mapped_x < curve_x * sqrt(1.0 - mapped_y*mapped_y)) is_shadow = true;
                }
            } else {
                // Waning (0.5 to 1.0) - Left side stays lit, shadow comes from right
                float progress = (phase - 0.5) * 2.0; // 0.0 to 1.0
                // progress 0.0 = full moon
                // progress 0.5 = right half shadowed (last quarter)
                // progress 1.0 = fully shadowed
                
                float curve_x = -cos(progress * 3.14159); // -1.0 up to 1.0
                
                if (progress < 0.5) {
                    // Gibbous (shadow is concave on right)
                    if (mapped_x > 0.0 && mapped_x > curve_x * sqrt(1.0 - mapped_y*mapped_y)) is_shadow = true;
                } else {
                    // Crescent (shadow is convex on left)
                    if (mapped_x > 0.0) is_shadow = true;
                    else if (mapped_x > curve_x * sqrt(1.0 - mapped_y*mapped_y)) is_shadow = true;
                }
            }
            
            if (is_shadow) {
                // Darken the shadowed area significantly but don't make it totally black
                ALBEDO *= 0.1;
            }
        }
    }
}
