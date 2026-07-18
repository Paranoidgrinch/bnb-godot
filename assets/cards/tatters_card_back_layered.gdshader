shader_type canvas_item;
render_mode unshaded;

// Production-oriented compositor. Supply four equally sized textures:
// opaque background, transparent rift, transparent Numina, transparent frame.
uniform sampler2D background_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D rift_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D numina_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D frame_tex : source_color, filter_linear, repeat_disable;

uniform float cycle_seconds : hint_range(1.0, 30.0, 0.1) = 12.0;
uniform float rift_warp : hint_range(0.0, 0.02, 0.0001) = 0.0035;
uniform float numina_drift : hint_range(0.0, 0.02, 0.0001) = 0.0025;
uniform float glow_pulse : hint_range(0.0, 1.0, 0.01) = 0.18;

// Set a different phase on each visible card without duplicating the material.
instance uniform float phase : hint_range(0.0, 1.0, 0.001) = 0.0;


vec4 alpha_over(vec4 under, vec4 over) {
	float alpha = over.a + under.a * (1.0 - over.a);
	vec3 rgb = mix(under.rgb, over.rgb, over.a);
	return vec4(rgb, alpha);
}


void fragment() {
	float loop_phase = fract(TIME / cycle_seconds + phase);
	float angle = loop_phase * TAU;

	// Two closed, integer-frequency motions keep the twelve-second loop seamless.
	vec2 rift_flow = vec2(
		sin(angle + UV.y * 15.0),
		cos(angle * 2.0 + UV.x * 11.0)
	) * rift_warp;
	vec2 spirit_flow = vec2(sin(angle), cos(angle)) * numina_drift;

	vec4 background = texture(background_tex, UV);
	vec4 rift = texture(rift_tex, UV + rift_flow);
	vec4 numina = texture(numina_tex, UV + spirit_flow);
	vec4 frame = texture(frame_tex, UV);

	float pulse = 0.5 - 0.5 * cos(angle);
	numina.rgb *= 1.0 + pulse * glow_pulse;
	rift.rgb *= 1.0 + pulse * glow_pulse * 0.35;

	vec4 composed = alpha_over(background, rift);
	composed = alpha_over(composed, numina);
	composed = alpha_over(composed, frame); // Frame stays sharp and continuous.
	COLOR = composed;
}
