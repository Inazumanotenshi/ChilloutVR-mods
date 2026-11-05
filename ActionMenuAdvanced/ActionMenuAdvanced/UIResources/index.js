const totalwidth = 500; // full width of actionmenu in pixels
const mid = 250; // half width in screen px
const STAGE_TOP = 130; // so weit beginnt das Menue unter dem oberen Rand (Platz fuer die Textbox)
const maxdist = 0.15; // 'inner' joystick zone, normalized
const deadzone = 0.5; // normalized
const pi = Math.PI;
const pi2 = 2 * Math.PI;
const clamp = (min, max, v) => Math.min(max, Math.max(min, v));

const $actionmenu = document.getElementById("actionmenu");
const $joystick = $actionmenu.getElementsByClassName("joystick")[0];
const $segments = $actionmenu.getElementsByClassName("segments")[0];
const $inside = $actionmenu.getElementsByClassName("inside")[0];
const $items = $actionmenu.getElementsByClassName("items")[0];

var quickmenu_active = false;
var menu_name;
var menu;
var menus = {}; // dynamically loaded
var settings = {}; // dynamically loaded
var breadcrumb = []; // list of entered menu
var sectors = 0; // number of items in the menu
var selected_sector = null;
var gameData = {};
var active_widget = null;
var wait_joystick_recenter = false; // prevent users to select items by mistake for certain widgets
var was_in_deadzone = false;
var update_to_items = {}; // items registry to sync game<>menu state


/* ===== ActionMenuAdvanced: Hexagon-Menue =====
   Winkel-Konvention hier: 0 = oben, im Uhrzeigersinn, in Grad
   Koordinaten in px innerhalb der 500x500 grossen Menue-Flaeche, Mitte = (mid, mid) */

const HEX_R_OUT = 246;      // Aussenradius (Spitze oben)
const HEX_R_IN = 60;        // Radius des Mittel-Hexagons
const HEX_GAP = 6;          // Breite der Luecken zwischen Segmenten
const HYSTERESIS_DEG = 3;   // so viele Grad muss man ins Nachbarsegment, bevor die Auswahl wechselt
const NARROW_DEG = 24;      // ab hier schmaler: Namen werden gedreht
const LABEL_FONT_MAX = 22, LABEL_FONT_MIN = 13;

let accent_rgb = [169, 40, 0];     // CVR-Standard #a92800, wird von der Mod mit deiner UI-Farbe ueberschrieben
let base_rgb = [34, 34, 34];       // CVR-Standard #222222, mit "Colourize Menu" deine erste Spielerfarbe (abgedunkelt)
let $segs = [];                      // ein div pro Menuepunkt

const deg2rad = d => d * pi / 180;
const dir = a => [Math.sin(deg2rad(a)), -Math.cos(deg2rad(a))]; // Einheitsvektor fuer Winkel a
const dot2 = (a, b) => a[0]*b[0] + a[1]*b[1];

function hex_vertices(R) {
	return [0, 60, 120, 180, 240, 300].map(a => { const d = dir(a); return [R*d[0], R*d[1]]; });
}

// Abstand von der Mitte bis zum Hexagon-Rand in Richtung a
function hex_edge_dist(R, a) {
	const local = ((((a % 60) + 60) % 60) - 30);
	return R * Math.cos(deg2rad(30)) / Math.cos(deg2rad(local));
}

// groesstes t, an dem die Linie o + t*d den Hexagon-Rand schneidet (Austrittspunkt)
function hex_exit(R, o, d) {
	const v = hex_vertices(R);
	let best = null;
	for (let k = 0; k < 6; k++) {
		const p = v[k], q = v[(k + 1) % 6];
		const e = [q[0] - p[0], q[1] - p[1]];
		const den = d[0]*e[1] - d[1]*e[0];
		if (Math.abs(den) < 1e-9) continue;
		const w = [p[0] - o[0], p[1] - o[1]];
		const t = (w[0]*e[1] - w[1]*e[0]) / den;
		const u = (w[0]*d[1] - w[1]*d[0]) / den;
		if (u >= -1e-6 && u <= 1 + 1e-6 && (best == null || t > best)) best = t;
	}
	return [o[0] + best*d[0], o[1] + best*d[1]];
}

function angle_of(p) { return ((Math.atan2(p[0], -p[1]) * 180 / pi) + 360) % 360; }

// Polygon fuer Segment i von n (Punkte relativ zur Mitte)
function segment_polygon(i, n, R_OUT = HEX_R_OUT, R_IN = HEX_R_IN) {
	if (n <= 1) { // ganzer Ring: aussen im, innen gegen den Uhrzeigersinn, verbunden ueber einen unsichtbaren Schlitz
		const o = hex_vertices(R_OUT), r = hex_vertices(R_IN);
		return [...o, o[0], r[0], r[5], r[4], r[3], r[2], r[1], r[0]];
	}
	const w = 360 / n, c = i * w, a0 = c - w/2, a1 = c + w/2, g = HEX_GAP / 2;
	const d0 = dir(a0), n0 = dir(a0 + 90), o0 = [n0[0]*g, n0[1]*g];
	const d1 = dir(a1), n1 = dir(a1 - 90), o1 = [n1[0]*g, n1[1]*g];
	const inside = p => dot2([p[0]-o0[0], p[1]-o0[1]], n0) > 0 && dot2([p[0]-o1[0], p[1]-o1[1]], n1) > 0
		&& ((angle_of(p) - a0 + 720) % 360) < w;
	const sorted = (R, desc) => hex_vertices(R).filter(inside)
		.sort((p, q) => (((angle_of(p) - a0 + 720) % 360) - ((angle_of(q) - a0 + 720) % 360)) * (desc ? -1 : 1));
	return [hex_exit(R_OUT, o0, d0), ...sorted(R_OUT, false), hex_exit(R_OUT, o1, d1),
		hex_exit(R_IN, o1, d1), ...sorted(R_IN, true), hex_exit(R_IN, o0, d0)];
}

const to_clip_path = (pts, cx = mid, cy = mid) => "polygon(" + pts.map(([x, y]) =>
	`${((cx + x) / totalwidth * 100).toFixed(3)}% ${((cy + y) / totalwidth * 100).toFixed(3)}%`).join(", ") + ")";

const rgba = (rgb, a) => `rgba(${rgb[0]}, ${rgb[1]}, ${rgb[2]}, ${a})`;

function paint_segment(i) {
	const $s = $segs[i];
	if (!$s || !menu) return;
	const item = menu[i];
	const hover = i === selected_sector;
	// Farben wie in der CVR-UI: Panels #222222 mit ~75 % Deckkraft, Hover voll deckend, aktiv = UI-Farbe
	if (item?.enabled)
		$s.style.backgroundColor = rgba(accent_rgb, hover ? 1 : 0.88);
	else
		$s.style.backgroundColor = rgba(base_rgb, hover ? 1 : 0.75);
}

function repaint_all_segments() { $segs.forEach((_, i) => paint_segment(i)); }

// Joystick-Richtung -> Segment, mit Hysterese gegen Zittern an den Grenzen
function pick_index(x, y, n, current) {
	if (n <= 1) return 0;
	const a = ((Math.atan2(x, y) * 180 / pi) + 360) % 360; // y zeigt hier nach oben
	const w = 360 / n;
	if (current != null) {
		const d = Math.abs(((a - current * w) % 360 + 540) % 360 - 180);
		if (d <= w/2 + HYSTERESIS_DEG) return current;
	}
	return Math.round(a / w) % n;
}
const pick_sector = (x, y) => pick_index(x, y, sectors, selected_sector);

// wie CVRs ViewColourHandler: zu helle Farben auf eine maximale Helligkeit abdunkeln
function parse_hex_limited(hex, max_luma) {
	if (typeof hex !== "string") return null;
	const m = hex.replace('#', '').match(/^([0-9a-fA-F]{6})/);
	if (!m) return null;
	const v = parseInt(m[1], 16);
	let rgb = [(v >> 16) & 255, (v >> 8) & 255, v & 255];
	const luma = 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2];
	if (luma > max_luma) rgb = rgb.map(c => Math.floor(c * max_luma / luma));
	return rgb;
}

function set_ui_colors(primary_hex, accent_hex, primary_max_luma) {
	const p = parse_hex_limited(primary_hex, primary_max_luma ?? 20);
	if (p) {
		base_rgb = p;
		document.documentElement.style.setProperty('--hex-base', rgba(base_rgb, 0.75));
		document.documentElement.style.setProperty('--hex-hover', rgba(base_rgb, 1));
	}
	set_accent_color(accent_hex);
}

function set_accent_color(hex) {
	const rgb = parse_hex_limited(hex, 120);
	if (!rgb) return;
	accent_rgb = rgb;
	document.documentElement.style.setProperty('--accent', rgba(accent_rgb, 1));
	document.documentElement.style.setProperty('--accent-soft', rgba(accent_rgb, 0.8));
	repaint_all_segments();
}

// Name und (wenn Platz ist) Icon im Segment platzieren
function place_item_label($item, i, n) {
	const w = 360 / n, c = i * w;
	const $icon = $item.getElementsByClassName("icon")[0];
	const $label = $item.getElementsByClassName("label")[0];
	const text_len = ($label?.textContent ?? "").length || 1;
	let x, y, rotate = 0, avail;

	if (n <= 1) {
		x = 0; y = -(HEX_R_IN + HEX_R_OUT * Math.cos(deg2rad(30))) / 2 - 20; avail = 300;
	}
	else {
		const ro = hex_edge_dist(HEX_R_OUT, c);
		const narrow = w < NARROW_DEG;
		let rm;
		if (narrow) {
			rm = (HEX_R_IN + ro) / 2 + 10;
			rotate = c - 90;                 // Text laeuft entlang des Segments nach aussen
			if (c > 180) rotate = c + 90;    // linke Seite: nicht auf dem Kopf
			avail = 2 * Math.min(ro - rm - 10, rm - HEX_R_IN - 10);
		} else {
			rm = ro * 0.64;
			avail = 2 * rm * Math.tan(deg2rad(Math.min(w, 120) / 2)) - 14;
		}
		const d = dir(c);
		x = rm * d[0]; y = rm * d[1];
	}

	// Icon nur, wenn vorhanden und genug Platz da ist
	const show_icon = $icon && rotate === 0 && avail >= 80 && (n <= 1 || 360 / n >= 30);
	if ($icon) $icon.style.display = show_icon ? 'block' : 'none';

	// Schrift so gross wie moeglich, ohne ueber das Segment zu ragen (Schaetzung: Zeichen * 0.55em)
	const fs = clamp(LABEL_FONT_MIN, LABEL_FONT_MAX, avail / (text_len * 0.55));
	if ($label) $label.style.fontSize = fs.toFixed(1) + 'px';

	$item.style.left = (mid + x) + 'px';
	$item.style.top = (mid + y) + 'px';
	$item.style.transform = `translate(-50%, -50%) rotate(${rotate.toFixed(2)}deg)`;
}


// convention is bottom-left is (-1,-1) and top-right is (+1,+1)
function handle_direction(x, y) { // values between -1 and +1
	if (!quickmenu_active) return;

	const dist = Math.sqrt(x*x + y*y);
	const is_in_deadzone = dist <= deadzone;

	if (wait_joystick_recenter) {
		if (is_in_deadzone)
			wait_joystick_recenter = false;
		else
			return;
	}

	const f = active_widget?.handle_direction
		? active_widget.handle_direction // widget has taken over
		: handle_direction_main;

	f(x, y, dist);
	was_in_deadzone = is_in_deadzone;
}

function sector_rotation(sector) {
	const rounded = pi + (sector - 0.5) * pi2 / sectors;
	return rounded * 180 / pi;
}


function coords_to_angle(x, y, dist) { // x,y from -1 to +1
	const divisor =  dist + y;
	const angle = Math.abs(divisor) <= 0.0001 // protection for division by 0
		? 0.0
		: 2* Math.atan(x / divisor) + pi;
	return angle; // in radians
}

function handle_direction_main(x, y, dist) { // x,y from -1 to +1
	const old_selected_sector = selected_sector;

	if (dist >= deadzone) {
		selected_sector = pick_sector(x, y);
	}
	else { // deadzone = no selection
		if (settings.flick_selection && !was_in_deadzone && selected_sector != null) {
			handle_click_main(); // magics
			return;
		}
		selected_sector = null;
	}

	// Joystick-Punkt bleibt im Mittel-Hexagon
	const jr = 0.6 * HEX_R_IN / totalwidth;
	$joystick.style.left = 100*(0.5 + jr * clamp(-1, 1, x)) + '%';
	$joystick.style.top  = 100*(0.5 - jr * clamp(-1, 1, y)) + '%'; // html convention has vertical axis inverted

	if (old_selected_sector != selected_sector) {
		// cohtml erkennt CSS-Aenderungen an Kindern nicht zuverlaessig, deshalb Klassen an allen Kindern
		const $old = $items.childNodes[old_selected_sector];
		if ($old) [$old, ...Array.from($old.childNodes)].forEach($el => $el.classList.remove('hover'));
		const $new = $items.childNodes[selected_sector];
		if ($new) [$new, ...Array.from($new.childNodes)].forEach($el => $el.classList.add('hover'));
		paint_segment(old_selected_sector);
		paint_segment(selected_sector);
	}

	if (old_selected_sector != selected_sector) {
		appcall("PlayCoreUiSound", "Hover");
		appcall("handVibrateGlobal", 0, 0.1, 10, 0.5); // delay, duration, frequency, amplitude
	}
}

function handle_click() {
	if (!quickmenu_active) return;

	if (active_widget?.handle_click)
		return active_widget.handle_click();

	return handle_click_main();
}

const virtual_back_item = {
	"name": "Back",
	"icon": "icon_back.svg",
	"action": {"type": "back"},
}

function handle_click_main() {
	const item = selected_sector != null
		? menu[selected_sector]
		: (settings.boring_back_button ? null : virtual_back_item );

	if (item == null) return;
	//console.log(['click selected_sector', selected_sector, item.name, item]);

	const $item = selected_sector != null ? $items.childNodes[selected_sector] : $inside;

	const action = item.action;
	let action_toggle = false;
	const is_enabled = !!item.enabled;

	switch (action.type) {
		case 'menu': {
			const current_menu = menu_name;
			load_menu(action.menu);
			breadcrumb.push(current_menu);
			break;
		}

		case 'dynamic menu': {
			const current_menu = menu_name;
			load_dynamic_menu(action.menu);
			breadcrumb.push(current_menu);
			break;
		}

		case 'back': {
			const last_menu_name = breadcrumb.pop();
			if (last_menu_name != undefined) // if fail: main menu probably
				load_menu(last_menu_name);
			break;
		}

		case 'system call': {
			const args = action.event_arguments ?? [];
			appcall(action.event, ...args);
			action_toggle = !!action.toggle;
			break;
		}

		case 'game settings toggle': {
			if (action.parameter === 'GeneralShowHud') // CVR reagiert nicht mehr live auf diese Einstellung
				engine.call("SetHudVisible", !is_enabled);
			else
				engine.call("CVRAppCallSaveSetting", action.parameter, is_enabled ? "False" : "True");
			action_toggle = true;
			break;
		}

		case 'game settings int': {
			// TODO: radial default value bugs, always 0?
			control_type_radial(item, action,
				v => engine.call("CVRAppCallSaveSetting", action.parameter, Math.round(v).toString()));
			break;
		}

		case 'set melon preference': {
			switch (action.control) {
				case undefined:
				case 'toggle': {
					const new_value = item.enabled ? 0 : (action.value ?? 1);
					engine.call("SetMelonPreference", action.parameter, String(new_value));
					action_toggle = action?.toggle;
					break;
				}

				case 'radial': {
					control_type_radial(item, action,
						v => engine.call("SetMelonPreference", action.parameter, String(v)));
					break;
				}

				case 'number': {
					control_type_number(item, action,
						v => engine.call("SetMelonPreference", action.parameter, String(v)));
					break;
				}

				case 'joystick_2d':
				case 'input_vector_2d': {
					control_type_2d(item, action,
						(x, y) => engine.call("SetMelonPreference", action.parameter, `${x},${y}`));
					break;
				}

				default:
					throw `unsupported control type: ${action.control}`;
			}
			break;
		}

		case 'callback': {
			switch (action.control) {
				case undefined:
				case null: {
					engine.call("ItemCallback", action.parameter)
					break;
				}

				case 'toggle': {
					action_toggle = true;
					engine.call("ItemCallback_bool", action.parameter, !item.enabled);
					break;
				}

				case 'impulse': {
					if (control_type_impulse(item, action,
						v => engine.call("ItemCallback_bool", action.parameter, v))
						)
						return;
					break;
				}

				case 'radial': {
					control_type_radial(item, action,
						v => engine.call("ItemCallback_float", action.parameter, v));
					break;
				}

				case 'number': {
					control_type_number(item, action,
						v => engine.call("ItemCallback_float", action.parameter, v));
					break;
				}

				case 'joystick_2d':
				case 'input_vector_2d': {
					control_type_2d(item, action,
						(x, y) => engine.call("ItemCallback_float_float", action.parameter, x, y));
					break;
				}

				default:
					throw `unsupported control type: ${action.control}`;
			}
			break;
		}

		case 'avatar parameter': {
			switch (action.control) {
				case 'toggle': {
					action_toggle = true;
					const new_value = item.enabled ? 0 : (action.value ?? 1);
					appcall("AppChangeAnimatorParam", action.parameter, new_value);
					break;
				}

				case 'impulse': {
					if (control_type_impulse(item, action,
						v => appcall("AppChangeAnimatorParam", action.parameter, v))
						)
						return;
					break;
				}

				case 'radial': {
					control_type_radial(item, action,
						v => appcall("AppChangeAnimatorParam", action.parameter, v));
					break;
				}

				case 'number': {
					control_type_number(item, action,
						v => appcall("AppChangeAnimatorParam", action.parameter, v));
					break;
				}

				case 'joystick_2d':
				case 'input_vector_2d': {
					control_type_2d(item, action, (denormalized_x, denormalized_y) => {
						appcall("AppChangeAnimatorParam", action.parameter + '-x', denormalized_x);
						appcall("AppChangeAnimatorParam", action.parameter + '-y', denormalized_y);
					});
					break;
				}

				default:
					throw `unsupported control type: ${action.control}`;
			}
			break;
		}

		default:
			throw `Unknown action: ${action.type} item ${item.name}`;
	}

	if (action.exclusive_option) {
		clear_all_enabled_sectors(item.action?.parameter);
		menu.forEach(i => {
			if (i != item && i.action?.parameter == action?.parameter)
				i.enabled = false;
		});
	}
	if (action_toggle)
		set_item_enabled(selected_sector, item, !is_enabled);

	repaint_all_segments();
	if ($item?.parentNode != null && $item === $inside)
		trigger_animation($item, "animated-item");

	appcall("PlayCoreUiSound", "Click");
}

function control_type_impulse(item, action, set_value) {
	if (item.enabled) return true; // prevent spam
	const sector = selected_sector;
	set_item_enabled(sector, item, true);
	set_value(action.value ?? 1);
	setTimeout(() => {
		if (!item.enabled) return;
		set_item_enabled(sector, item, false);
		set_value(action.default_value ?? 0);
		appcall("PlayCoreUiSound", "Click");
	}, (action.duration ?? 1) * 1000);
}

function control_type_radial(item, action, set_value) {
	const min_value = action.min_value ?? 0;
	const max_value = action.max_value ?? 1;
	const delta = max_value - min_value;
	const start_value = ((action.default_value ?? 0) - min_value) / delta;
	widget_radial.start(item, start_value, (v) => {
		const denormalized = v * delta + min_value;
		set_value(denormalized);
		action.default_value = denormalized; // kinda cheating but works
	});
	wait_joystick_recenter = true;
}

// freie Zahleneingabe; min_value/max_value sind optional (ohne = unbegrenzt)
function control_type_number(item, action, set_value) {
	widget_numpad.start(item, action, (v) => {
		set_value(v);
		action.default_value = v;
	});
	wait_joystick_recenter = true;
}

function control_type_2d(item, action, set_values) {
	const min_value_x = action.min_value_x ?? 0;
	const max_value_x = action.max_value_x ?? 1;
	const delta_x = max_value_x - min_value_x;
	const start_value_x = ((action.default_value_x ?? 0) - min_value_x) / delta_x;

	const min_value_y = action.min_value_y ?? 0;
	const max_value_y = action.max_value_y ?? 1;
	const delta_y = max_value_y - min_value_y;
	const start_value_y = ((action.default_value_y ?? 0) - min_value_y) / delta_y;

	const fmt2 = v => Number(v ?? 0).toFixed(2);
	const label = () => `X ${fmt2(action.default_value_x)}\u2003Y ${fmt2(action.default_value_y)}`;

	if (action.control == 'joystick_2d') {
		widget_j2d.start(item, start_value_x, start_value_y, (x, y) => {
			const denormalized_x = x * delta_x + min_value_x;
			const denormalized_y = y * delta_y + min_value_y;
			set_values(denormalized_x, denormalized_y);
			action.default_value_x = denormalized_x; // kinda cheating but works
			action.default_value_y = denormalized_y;
		}, label);
	}
	else if (action.control == 'input_vector_2d') {
		const delta_scale = 0.001;
		let last_value_x = start_value_x;
		let last_value_y = start_value_y;
		let last_time = new Date().getTime();

		// always start in the middle because it works in relative coords
		widget_j2d.start(item, 0.5, 0.5, (x, y) => {
            const time_now = new Date().getTime();
			const scale = delta_scale * (time_now - last_time)
			// reconvert from 0,1 to -1,+1 and scale
			const denormalized_x = clamp(min_value_x, max_value_x,
				last_value_x + scale * (2*x - 1) * delta_x);
			const denormalized_y = clamp(min_value_y, max_value_y,
				last_value_y + scale * (2*y - 1) * delta_y);
			set_values(denormalized_x, denormalized_y);
			last_value_x = denormalized_x;
			last_value_y = denormalized_y;
			action.default_value_x = last_value_x;
			action.default_value_y = last_value_y;
			last_time = time_now;
		}, label);
	}
	wait_joystick_recenter = true;
}

function back_from_widget() {
	trigger_animation($inside, "animated-menu");

	if (settings.flick_selection) {
		selected_sector = null;
		wait_joystick_recenter = true; // security
	}

	active_widget = null; // back
}

document.addEventListener('mousemove', (event) => {
	if (settings.in_vr) return;
	let x = (event.clientX - mid);
	let y = (event.clientY - STAGE_TOP - mid);
	const dist = Math.sqrt(x*x + y*y);

	// normalized and clamped to distance 1
	const distnorm = dist / totalwidth;
	if (distnorm > maxdist) {
		x /= dist;
		y /= dist;
	} else {
		const scale = maxdist * totalwidth;
		x /= scale;
		y /= scale;
	}

	handle_direction(x, -y); // html convention has vertical axis inverted
});

document.addEventListener('mouseup', (event) => {
	if (settings.in_vr || event.button != 0) return;
	handle_click();
});


function appcall(type, arg1, arg2, arg3, arg4) {
	// yes we need to convert all to string because they decided one fits all
	arg1 = arg1?.toString() || null;
	arg2 = arg2?.toString() || null;
	arg3 = arg3?.toString() || null;
	arg4 = arg4?.toString() || null;
	// yes this function needs all those args even if they're null
	//console.log("CVRAppCallSystemCall", type, arg1, arg2, arg3, arg4);
	engine.call("CVRAppCallSystemCall", type, arg1, arg2, arg3, arg4);
}

function set_item_enabled(sector, item, new_value) {
	item.enabled = new_value;
	show_item_enabled(sector, item);
}

function show_item_enabled(sector, item) {
	if (sector == null || item == null) throw `sector ${sector} or item ${item} is null`;
	item.enabled = item.enabled ?? false;
	if (menu && menu[sector] === item)
		paint_segment(sector);
}

// WARN: this doesn't support Object in any of the values!
const action_to_update_key = (action) => 
	['type', 'event', 'event_arguments', 'parameter']
	.map(k => String(action[k]))
	.join('\0');

function on_game_value_update(update) {
	if (update_to_items.length == 0) return; // not init yet
	const k = action_to_update_key(update);
	const items = update_to_items[k];
	if (!items) return;

	items.forEach(item => {
		const action = item.action;

		// Avatar-Parameter, die sich ausserhalb des Menues geaendert haben
		if (update.type === 'avatar parameter') {
			const c = action.control;
			if ((c === 'toggle' || c === 'impulse') && update.value != null)
				item.enabled = Math.abs(Number(action.value ?? 1) - Number(update.value)) < 0.001;
			else if ((c === 'radial' || c === 'number') && update.default_value != null)
				action.default_value = update.default_value;
			else if ((c === 'joystick_2d' || c === 'input_vector_2d') && update.default_value_x != null) {
				action.default_value_x = update.default_value_x;
				action.default_value_y = update.default_value_y;
			}
			return;
		}

		if (action.toggle)
			item.enabled = (action.value ?? true) == update.value;
		['default_value', 'default_value_x', 'default_value_y'] // TODO: support more?
		.forEach(k => {
			const v = update[k];
			if (v != null)
			action[k] = v;
		});
	});

	// update current menu in case an item is currently visible
	menu.forEach((item, i) => {
		if (items.includes(item))
			show_item_enabled(i, item);
	})
}

function build_$item(item, i) {
	const $item = document.createElement('div');
	$item.className = "item";

	if (item.icon != null) {
		const $icon = document.createElement('img');
		$icon.src = item.icon;
		if (item.icon.startsWith("data:")) // help cohtml with lazily-loaded images
			$icon.width = $icon.height = 75;
		// TODO: add event onload to resize when it's ready???
		$icon.className = "icon";
		$item.appendChild($icon);
	}

	if (item.name != null) {
		const $label = document.createElement('div');
		$label.innerHTML = item.name;
		$label.className = "label";
		$item.appendChild($label);
	}

	if (i != null && item.enabled)
		show_item_enabled(i, item);

	return $item;
}

function clear_all_enabled_sectors(parameter) {
	repaint_all_segments(); // Segmente zeigen ihren Zustand direkt, hier gibt es nichts mehr aufzuraeumen
}

function load_menu(name) {
	if (menus[name] == null) throw `Menu ${name} not found`;
	menu = [...menus[name]]; // make a shallow copy: we can add dynamic elements like the "back" button
	menu_name = name;

	// boring back button is part of the radial
	if (menu_name != "main" && settings.boring_back_button)
		menu.unshift(virtual_back_item);

	$items.innerHTML = '';
	$segments.innerHTML = '';
	$segs = [];
	selected_sector = null;

	sectors = menu.length;

	// Hexagon-Segmente bauen
	menu.forEach((item, i) => {
		const $s = document.createElement('div');
		$s.className = "segment";
		$s.style.clipPath = to_clip_path(segment_polygon(i, sectors));
		$segments.appendChild($s);
		$segs.push($s);
	});

	// Namen und Icons
	menu.forEach((item, i) => {
		const $item = build_$item(item, i);
		place_item_label($item, i, sectors);
		trigger_animation($item, "fade-in-item");
		$items.appendChild($item);
	});
	repaint_all_segments();

	// Zurueck-Knopf in der Mitte
	if (menu_name != "main" && !settings.boring_back_button) {
		const $item = build_$item(virtual_back_item);
		$item.classList.add("center-item");
		$item.style.left = $item.style.top = mid + 'px';
		$items.appendChild($item); // it's not added to the radial itself
	}

	// animation weeeeeeee
	trigger_animation($inside, "animated-menu");
}

function trigger_animation($el, animation) {
	$el.classList.add(animation);
	$el.addEventListener('animationend', (event) => {
		$el.classList.remove(animation);
	}, {'once': true});
}


function compute_radial_mask(angle) { // angle in radians
	// full circle = no mask
	if (angle == pi2) return "none";

	const quadrant = Math.floor(2 * angle / pi) % 4;
	const x = 50 * (1 + Math.sin(angle)); // coordinates are computed in %
	const y = 50 * (1 + Math.cos(pi - angle));
	// we're computing a polygon mask to only show the visible arc of a circle
	let points = [];
	if (quadrant <= 1) {
		// 0.001 is necessary due to a graphical bug when y=0
		points = [ [50,0], [50,50], [x, y], [100, y+0.001], [100, 0] ];
	}
	// depending on angle we have to add more points to fit all sections of the circle
	else if (quadrant <= 2) {
		points = [ [50,0], [50,50], [x, y], [x, 100], [100, 100], [100, 0] ];
	}
	else {
		points = [ [50,0], [50,50], [x, y], [0, y], [0, 100], [100, 100], [100, 0] ];
	}

	// format as css clipPath string
	return "polygon("+ points.map(([x, y]) => `${x}% ${y}%`).join(" , ") + ")";
}


/* radial widget */

const widget_radial = (function() {
	const $w = document.getElementById("widget-radial");
	const $arc = $w.getElementsByClassName("arc")[0];
	const $indicator = $w.getElementsByClassName("indicator")[0];
	const $center = $w.getElementsByClassName("center")[0];
	const $value = $w.getElementsByClassName("value")[0];
	const $inside = $w.getElementsByClassName("inside")[0];

	let last_angle = 0;

	const handle_click_radial = () => {
		$w.style.display = 'none';

		back_from_widget();
	}

	const handle_direction_radial = (set_value, x, y, dist) => {
		if (dist >= deadzone) {
			let angle = coords_to_angle(-x, -y, dist); // start at the top, right to left = inverted

			// protect from unintended big change 0<>100%
			if (Math.abs(last_angle - angle) > pi) {
				// freeze only close to 0 or 360°
				if (angle <= pi/2)
					angle = pi2;
				else if (angle >= 3*pi/2)
					angle = 0;
			}
			last_angle = angle;

			widget_radial_set(angle);
			$indicator.style.left = 100 * (0.5 + maxdist * Math.sin(angle)) + '%';
			$indicator.style.top  = 100 * (0.5 + maxdist * Math.cos(pi - angle)) + '%';

			const value = angle / pi2;
			set_value(value); // output between 0 and 1
			$value.innerHTML = value_label(value);
		}
		// else: deadzone = no update
	}

	const widget_radial_set = (angle) => {
		const clip_path = compute_radial_mask(angle);
		$arc.style.clipPath = clip_path;
	}

	const value_label = value => Math.round(value * 100) + "%";

	const start = (item, start_value, set_value) => { // takes normalized value (0 to 1)
		$w.style.display = 'block';

		$center.innerHTML = "";
		const $item = build_$item(item);
		$center.appendChild($item);

		const handle_direction = (x, y, dist) => handle_direction_radial(set_value, x, y, dist);
		const angle = pi2 * start_value
		widget_radial_set(angle);
		last_angle = angle;
		$value.innerHTML = value_label(start_value);
		active_widget = {
			handle_direction: handle_direction,
			handle_click: handle_click_radial,
		};

		$indicator.style.left = $indicator.style.top = '50%'; // start in middle
		trigger_animation($inside, "animated-menu");
	}


	return {
		'start': start,
	};
})();


/* joystick 2D widget (ActionMenuAdvanced: Hexagon-Design) */

const widget_j2d = (function() {
	const $w = document.getElementById("widget-j2d");
	const $name = $w.getElementsByClassName("w-name")[0];
	const $value = $w.getElementsByClassName("w-value")[0];
	const $marker = $w.getElementsByClassName("j2d-marker")[0];
	const $guide_v = $w.getElementsByClassName("j2d-guide-v")[0];
	const $guide_h = $w.getElementsByClassName("j2d-guide-h")[0];
	const $triangles = $w.getElementsByClassName("triangles")[0];

	const R_MOVE = 164; // so weit wandert der Marker vom Mittelpunkt
	// Pfeile oben, rechts, unten, links (Stick-Koordinaten: y nach oben)
	const ARROWS = [[0, 1, 0], [1, 0, 90], [0, -1, 180], [-1, 0, 270]];
	let $tris = [];
	let label_fn = null;

	const build = () => {
		$triangles.innerHTML = ""; $tris = [];
		ARROWS.forEach(([dx, dy, a]) => {
			const $t = document.createElement('div');
			$t.className = "triangle";
			const r = hex_edge_dist(HEX_R_OUT, a) - 34, d = dir(a);
			$t.style.left = (mid + r * d[0]) + 'px';
			$t.style.top = (mid + r * d[1]) + 'px';
			$t.style.transform = `translate(-50%, -50%) rotate(${a}deg)`;
			$triangles.appendChild($t); $tris.push($t);
		});
	};

	const values_to_joystick = (x, y) => { // erwartet -1..+1, y nach oben
		const px = mid + x * R_MOVE, py = mid - y * R_MOVE;
		$marker.style.left = px + 'px';
		$marker.style.top = py + 'px';
		$guide_v.style.left = (px - 1) + 'px';
		$guide_v.style.top = Math.min(mid, py) + 'px';
		$guide_v.style.height = Math.abs(py - mid) + 'px';
		$guide_h.style.top = (py - 1) + 'px';
		$guide_h.style.left = Math.min(mid, px) + 'px';
		$guide_h.style.width = Math.abs(px - mid) + 'px';
		ARROWS.forEach(([dx, dy], i) => {
			const dot = Math.max(0, x * dx + y * dy);
			const size = 12 + 18 * dot;
			const $t = $tris[i];
			$t.style.borderLeft = $t.style.borderRight = size + 'px solid transparent';
			$t.style.borderBottom = (size * 1.35) + 'px solid ' + (dot > 0.05 ? rgba(accent_rgb, 0.5 + 0.5 * dot) : 'rgba(255, 255, 255, 0.35)');
		});
	};

	const update_label = () => { if (label_fn) $value.innerHTML = label_fn(); };

	const handle_direction_joystick_2d = (set_value, x, y, dist) => {
		const l = Math.sqrt(x*x + y*y);
		if (l > 1) { x /= l; y /= l; }
		values_to_joystick(x, y);
		set_value(0.5 * (1 + x), 0.5 * (1 + y)); // convert range -1,+1 to 0,1
		update_label();
	};

	const handle_click_joystick_2d = () => {
		$w.style.display = 'none';
		$actionmenu.style.display = 'block';
		back_from_widget();
	};

	const start = (item, start_value_x, start_value_y, set_value, _label_fn) => { // takes normalized values x,y both (0 to 1)
		if ($tris.length === 0) build();
		label_fn = _label_fn ?? null;
		$name.innerHTML = item.name ?? "";
		$w.style.display = 'block';
		$actionmenu.style.display = 'none'; // Hauptmenue ausblenden

		active_widget = {
			handle_direction: (x, y, dist) => handle_direction_joystick_2d(set_value, x, y, dist),
			handle_click: handle_click_joystick_2d,
		};

		// for joystick start position: convert normalized -> denormalized
		values_to_joystick(start_value_x * 2 - 1, start_value_y * 2 - 1);
		update_label();
	};

	return {
		'start': start,
	};
})();


/* Ziffernblock-Widget (ActionMenuAdvanced) */

const widget_numpad = (function() {
	const $w = document.getElementById("widget-numpad");
	const $meta_name = $w.getElementsByClassName("np-name")[0];
	const $meta_range = $w.getElementsByClassName("np-range")[0];
	const $text = $w.getElementsByClassName("np-text")[0];
	const $placeholder = $w.getElementsByClassName("np-placeholder")[0];
	const $segments = $w.getElementsByClassName("np-segments")[0];
	const $labels = $w.getElementsByClassName("np-labels")[0];
	const $ok = $w.getElementsByClassName("np-ok")[0];
	const $joystick = $w.getElementsByClassName("joystick")[0];

	// Hexagon etwas kleiner und tiefer, damit die Textbox direkt ueber die Spitze passt
	const R_OUT = HEX_R_OUT, R_IN = HEX_R_IN, CX = mid, CY = mid; // gleich gross wie das Hauptmenue
	const KEYS = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0", ".", "\u2212", "\u232B", "\u2715"];
	const MAX_CHARS = 10;

	let $segs = [], sel = null, in_center = false, typed = "", current = null, ctx = null;

	const fmt = v => (v == null || isNaN(v)) ? "0" : String(Math.round(Number(v) * 10000) / 10000);

	const paint = () => {
		$segs.forEach(($s, i) => $s.style.backgroundColor = rgba(base_rgb, i === sel ? 1 : 0.75));
		$ok.style.backgroundColor = rgba(accent_rgb, in_center ? 1 : 0.85);
		$text.innerHTML = typed.replace("-", "\u2212");
		$placeholder.innerHTML = typed === "" ? fmt(current) : "";
	};

	const build = () => {
		$segments.innerHTML = ""; $labels.innerHTML = ""; $segs = [];
		const n = KEYS.length, w = 360 / n;
		KEYS.forEach((k, i) => {
			const $s = document.createElement('div');
			$s.className = "segment";
			$s.style.clipPath = to_clip_path(segment_polygon(i, n, R_OUT, R_IN), CX, CY);
			$segments.appendChild($s); $segs.push($s);

			const c = i * w, r = hex_edge_dist(R_OUT, c) * 0.68, d = dir(c);
			const $l = document.createElement('div');
			$l.className = "np-key";
			// Loeschen und Abbrechen als Icons: CVRs Schrift hat diese Zeichen nicht
			const icon = { "\u232B": "icon_np_backspace.svg", "\u2715": "icon_np_cancel.svg" }[k];
			if (icon) {
				const $img = document.createElement('img');
				$img.src = icon;
				$img.className = "np-icon";
				$l.appendChild($img);
			}
			else {
				$l.innerHTML = k;
				if (k === "." || k === "\u2212") $l.classList.add("np-key-big"); // kleine Zeichen groesser
			}
			$l.style.left = (CX + r * d[0]) + 'px';
			$l.style.top = (CY + r * d[1]) + 'px';
			$labels.appendChild($l);
		});
		$ok.style.clipPath = to_clip_path(hex_vertices(R_IN - HEX_GAP), CX, CY);
		$w.getElementsByClassName("np-ok-label")[0].style.top = CY + 'px';
		$w.getElementsByClassName("np-header")[0].style.top = (CY - R_OUT - 6) + 'px';
		$joystick.style.top = CY + 'px';
	};

	const close = () => {
		$w.style.display = 'none';
		$actionmenu.style.display = 'block'; // Hauptmenue wieder zeigen
		ctx = null;
		back_from_widget();
	};

	const confirm = () => {
		if (typed !== "" && typed !== "-" && typed !== "-0.") {
			let v = parseFloat(typed);
			if (!isNaN(v)) {
				if (ctx.min != null) v = Math.max(ctx.min, v);
				if (ctx.max != null) v = Math.min(ctx.max, v);
				ctx.set_value(v);
				appcall("PlayCoreUiSound", "Click");
			}
		}
		close();
	};

	const press = (k) => {
		switch (k) {
			case "\u2715": close(); return;                      // abbrechen
			case "\u232B": typed = typed.slice(0, -1); break;    // loeschen
			case "\u2212": typed = typed.startsWith("-") ? typed.slice(1) : "-" + typed; break;
			case ".":
				if (!typed.includes(".")) typed += (typed === "" || typed === "-") ? "0." : ".";
				break;
			default:
				if (typed.replace("-", "").length < MAX_CHARS) typed += k;
		}
		appcall("PlayCoreUiSound", "Click");
		paint();
	};

	const handle_direction_numpad = (x, y, dist) => {
		const old_sel = sel, old_center = in_center;
		in_center = dist < deadzone;
		sel = in_center ? null : pick_index(x, y, KEYS.length, sel);
		const jr = 0.6 * R_IN / totalwidth;
		$joystick.style.left = 100*(0.5 + jr * clamp(-1, 1, x)) + '%';
		$joystick.style.top = (CY - jr * totalwidth * clamp(-1, 1, y)) + 'px';
		if (old_sel !== sel || old_center !== in_center) {
			appcall("PlayCoreUiSound", "Hover");
			appcall("handVibrateGlobal", 0, 0.1, 10, 0.5);
			paint();
		}
	};

	const handle_click_numpad = () => {
		if (in_center || sel == null) confirm();
		else press(KEYS[sel]);
	};

	// Tastatureingabe am Desktop (kommt von der Mod als Text)
	const handle_keys = (str) => {
		if (!ctx) return;
		for (const ch of str) {
			if (ch >= "0" && ch <= "9") press(ch);
			else if (ch === "." || ch === ",") press(".");
			else if (ch === "-") press("\u2212");
			else if (ch === "\b") press("\u232B");
			else if (ch === "\n" || ch === "\r") { confirm(); return; }
			else if (ch === "\u001b") { close(); return; }
		}
	};

	const start = (item, action, set_value) => {
		if ($segs.length === 0) build();
		ctx = {
			set_value: set_value,
			min: action.min_value ?? null,
			max: action.max_value ?? null,
		};
		typed = "";
		sel = null; in_center = true;
		current = action.default_value;
		$meta_name.innerHTML = item.name ?? "";
		const range = (ctx.min != null || ctx.max != null)
			? ` (${ctx.min != null ? fmt(ctx.min) : "\u2013"} \u2026 ${ctx.max != null ? fmt(ctx.max) : "\u2013"})` : "";
		$meta_range.innerHTML = `Current: ${fmt(current)}${range}`;
		$w.style.display = 'block';
		$actionmenu.style.display = 'none'; // Hauptmenue ausblenden, damit nichts durchscheint
		paint();
		active_widget = {
			handle_direction: handle_direction_numpad,
			handle_click: handle_click_numpad,
		};
	};

	return { start: start, handle_keys: handle_keys };
})();

engine.on('KeyInput', (str) => widget_numpad.handle_keys(str));


/* dispatchers */

function load_action_menu(_menu, _settings) {
    menus = _menu.menus;
	settings = _settings ?? {};
	console.log('load_action_menu menus:', Object.keys(_menu.menus));
	console.log('load_action_menu settings:', JSON.stringify(settings));

	if (settings.flick_selection)
		wait_joystick_recenter = true;

	// Debug-Anzeige
	const $dbg = document.getElementById("debug-footer");
	$dbg.style.display = settings.debug ? 'block' : 'none';
	$dbg.getElementsByClassName("debug-left")[0].innerHTML = "Version " + (settings.version ?? "?");

	// offene Widgets schliessen, falls das Menue neu geladen wird (z. B. Avatarwechsel)
	["widget-radial", "widget-j2d", "widget-numpad"].forEach(id => document.getElementById(id).style.display = 'none');
	$actionmenu.style.display = 'block';
	active_widget = null;

	// need an item registry to sync game<>menu update system
	update_to_items = {}; // bei neuem Avatar alte Eintraege verwerfen
	Object.values(menus).forEach(m => {
		m.forEach(item => {
			const k = action_to_update_key(item.action);
			const xs = update_to_items[k] = update_to_items[k] ?? [];
			xs.push(item);
		});
	});

	load_menu("main");
}

(function () {
	let waiting_for_menu;

	window.load_dynamic_menu = (name) => {
		waiting_for_menu = name;
		engine.call('RequestDynamicMenu', name);
	};

	engine.on('DynamicMenuData', (_menus) => {
		if (!waiting_for_menu) throw `Not waiting for a menu ${waiting_for_menu}`;

		const m = JSON.parse(_menus)
		if (m[waiting_for_menu] == null) throw `Waiting for ${waiting_for_menu} but not found`;

		Object.keys(m).forEach(k => {
			menus[k] = m[k];
		});

		const new_menu = waiting_for_menu;
		waiting_for_menu = null;
		load_menu(new_menu);
	});
})();

(function() {
	let last_trigger = false;

	engine.on('InputData', (_content) => {
		gameData = JSON.parse(_content);

		const joyvec = gameData.joystick;
		handle_direction(joyvec.x, joyvec.y);

		const trigger = gameData.trigger > 0.9;
		if (trigger && !last_trigger)
			handle_click();
		last_trigger = trigger;
	});
})();

engine.on('LoadActionMenu', (_content, _settings) => {
	load_action_menu(JSON.parse(_content), JSON.parse(_settings));
});

engine.on('ToggleActionMenu', (show) => {
	quickmenu_active = show;
});

engine.on('AccentColor', (hex) => set_accent_color(hex));
engine.on('UiColors', (primary, accent, max_luma) => set_ui_colors(primary, accent, max_luma));

engine.on('GameValueUpdate', (_update) => {
	on_game_value_update(JSON.parse(_update).action);
});


/* start */

if (window.navigator.appVersion != undefined) { // browser only
	fetch('actionmenu.json')
	.then((data) =>  data.json())
	.then((j) => {
		load_action_menu(j, {
			in_vr: false,
			boring_back_button: false,
			flick_selection: false,
		});
	});
	quickmenu_active = true;
} else {
	engine.trigger('ActionMenuReady');
}
