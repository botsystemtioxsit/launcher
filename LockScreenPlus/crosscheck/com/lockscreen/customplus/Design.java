/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.content.Context
 *  org.json.JSONArray
 *  org.json.JSONObject
 */
package com.lockscreen.customplus;

import android.content.Context;
import com.lockscreen.customplus.Store;
import com.lockscreen.customplus.SystemColors;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import org.json.JSONArray;
import org.json.JSONObject;

final class Design {
    static final String[] DATE_FORMATS;
    static final String[] DATE_NAMES;
    static final String[] SEPARATORS;
    static final String[] SEPARATOR_NAMES;
    static final String[] STYLE_NAMES;
    static final int UNSET = -9999;
    boolean autoColors = false;
    boolean batShow = false;
    int batSize = 100;
    float batX = 0.5f;
    Xf batXf;
    float batY = 0.55f;
    int bgMode = 0;
    int clockColor = -1;
    int clockColor2 = -1754827;
    boolean clockOver = false;
    boolean clockShow = true;
    int clockSize = 100;
    int clockStyle = 0;
    float clockX = 0.5f;
    Xf clockXf;
    float clockY = 0.3f;
    int dateFmt = 0;
    FontSpec dateFont;
    boolean dateShow = true;
    int dateSize = 100;
    float dateX = 0.5f;
    Xf dateXf;
    float dateY = 0.5f;
    int dim = 0;
    FontSpec font = new FontSpec();
    int grad1 = -14997707;
    int grad2 = -11830879;
    int gradAngle = 90;
    boolean gradAnim = true;
    boolean gradText = false;
    int hue = 0;
    final List<Layer> layers;
    int letterSpacing = 0;
    int lineGap = 40;
    boolean outline = false;
    int pAlpha = 100;
    int pColor = 0;
    int pSize = 100;
    int pSpeed = 100;
    boolean parallax = true;
    int parallaxStrength = 50;
    boolean parallaxText = false;
    int particleCount = 60;
    int particles = 0;
    int sat = 100;
    int separator = 0;
    boolean shadow = true;
    final List<Sticker> stickers;
    boolean stroke = false;
    int strokeColor = -16777216;
    int strokeW = 6;
    int[] sys;
    int textColor = -1;
    boolean twoTone = false;
    boolean use24 = true;
    boolean weatherAuto = false;
    String weatherCity = "";
    int weatherCode = -1;
    double weatherLat;
    double weatherLon;

    static {
        STYLE_NAMES = new String[]{"\u041a\u0440\u0443\u043f\u043d\u044b\u0435", "\u0414\u0432\u0443\u0445\u0446\u0432\u0435\u0442\u043d\u044b\u0435", "\u041a\u043e\u043d\u0442\u0443\u0440", "\u0421\u0442\u0440\u043e\u043a\u0430", "\u0421\u0442\u0440\u043e\u043a\u0430 \u0434\u0432\u0443\u0445\u0446\u0432\u0435\u0442\u043d\u0430\u044f", "\u041a\u043e\u043d\u0442\u0443\u0440\u043d\u0430\u044f \u0441\u0442\u0440\u043e\u043a\u0430", "\u0410\u043d\u0430\u043b\u043e\u0433\u043e\u0432\u044b\u0435", "\u041c\u043e\u043d\u043e", "\u0423\u0437\u043a\u0438\u0435", "\u0421 \u0437\u0430\u0441\u0435\u0447\u043a\u0430\u043c\u0438", "\u0422\u0435\u0445\u043d\u043e", "\u041f\u043b\u0430\u043a\u0430\u0442", "\u041f\u0438\u043a\u0441\u0435\u043b\u0438", "\u041c\u044f\u0433\u043a\u0438\u0435", "\u0412\u044b\u0441\u043e\u043a\u0438\u0435 \u0443\u0437\u043a\u0438\u0435", "\u0413\u0440\u0430\u0434\u0438\u0435\u043d\u0442"};
        DATE_FORMATS = new String[]{"EEEE, d MMMM", "d MMMM", "EEE, d MMM", "dd.MM.yyyy", "yyyy-MM-dd", "MMMM d"};
        DATE_NAMES = new String[]{"\u0412\u043e\u0441\u043a\u0440\u0435\u0441\u0435\u043d\u044c\u0435, 25 \u043e\u043a\u0442\u044f\u0431\u0440\u044f", "25 \u043e\u043a\u0442\u044f\u0431\u0440\u044f", "\u0412\u0441, 25 \u043e\u043a\u0442", "25.10.2026", "2026-10-25", "\u041e\u043a\u0442\u044f\u0431\u0440\u044c 25"};
        SEPARATORS = new String[]{":", ".", " ", ""};
        SEPARATOR_NAMES = new String[]{"11:11", "11.11", "11 11", "1111"};
    }

    Design() {
        this.clockXf = new Xf();
        this.dateFont = new FontSpec();
        this.dateXf = new Xf();
        this.batXf = new Xf();
        this.layers = new ArrayList<Layer>();
        this.stickers = new ArrayList<Sticker>();
        this.weatherLat = Double.NaN;
        this.weatherLon = Double.NaN;
    }

    static void applyStyle(Design design, int n) {
        design.clockShow = true;
        design.outline = false;
        design.twoTone = false;
        design.gradText = false;
        design.font = new FontSpec();
        switch (n) {
            default: {
                design.clockStyle = 0;
                design.font.id = "sys:2";
                design.gradText = true;
                break;
            }
            case 14: {
                design.clockStyle = 0;
                design.font.id = "asset:RobotoFlex.ttf";
                design.font.wght = 800;
                design.font.wdth = 28;
                design.font.hgt = 788;
                break;
            }
            case 13: {
                design.clockStyle = 0;
                design.font.id = "asset:Comfortaa.ttf";
                break;
            }
            case 12: {
                design.clockStyle = 1;
                design.font.id = "asset:PressStart2P.ttf";
                break;
            }
            case 11: {
                design.clockStyle = 0;
                design.font.id = "asset:Anton.ttf";
                design.twoTone = true;
                break;
            }
            case 10: {
                design.clockStyle = 1;
                design.font.id = "asset:Orbitron.ttf";
                design.twoTone = true;
                break;
            }
            case 9: {
                design.clockStyle = 1;
                design.font.id = "sys:4";
                break;
            }
            case 8: {
                design.clockStyle = 0;
                design.font.id = "sys:3";
                break;
            }
            case 7: {
                design.clockStyle = 1;
                design.font.id = "sys:5";
                break;
            }
            case 6: {
                design.clockStyle = 2;
                break;
            }
            case 5: {
                design.clockStyle = 1;
                design.font.id = "sys:2";
                design.outline = true;
                break;
            }
            case 4: {
                design.clockStyle = 1;
                design.font.id = "sys:2";
                design.twoTone = true;
                break;
            }
            case 3: {
                design.clockStyle = 1;
                break;
            }
            case 2: {
                design.clockStyle = 0;
                design.font.id = "sys:2";
                design.outline = true;
                break;
            }
            case 1: {
                design.clockStyle = 0;
                design.font.id = "sys:2";
                design.twoTone = true;
                break;
            }
            case 0: {
                design.clockStyle = 0;
            }
        }
        if ((design.twoTone || design.gradText) && design.clockColor2 == design.clockColor) {
            design.clockColor2 = -1754827;
        }
    }

    static Design fromJson(JSONObject jSONObject) {
        int n;
        Design design = new Design();
        design.bgMode = jSONObject.optInt("bgMode", design.bgMode);
        design.grad1 = jSONObject.optInt("grad1", design.grad1);
        design.grad2 = jSONObject.optInt("grad2", design.grad2);
        design.gradAnim = jSONObject.optBoolean("gradAnim", design.gradAnim);
        design.dim = jSONObject.optInt("dim", design.dim);
        design.hue = jSONObject.optInt("hue", design.hue);
        design.sat = jSONObject.optInt("sat", design.sat);
        design.clockShow = jSONObject.optBoolean("clockShow", design.clockShow);
        design.clockStyle = jSONObject.optInt("clockStyle", design.clockStyle);
        design.outline = jSONObject.optBoolean("outline", design.outline);
        design.twoTone = jSONObject.optBoolean("twoTone", design.twoTone);
        design.gradText = jSONObject.optBoolean("gradText", design.gradText);
        design.gradAngle = jSONObject.optInt("gradAngle", design.gradAngle);
        design.stroke = jSONObject.optBoolean("stroke", design.stroke);
        design.strokeW = jSONObject.optInt("strokeW", design.strokeW);
        design.strokeColor = jSONObject.optInt("strokeColor", design.strokeColor);
        design.shadow = jSONObject.optBoolean("shadow", design.shadow);
        design.clockColor = jSONObject.optInt("clockColor", design.clockColor);
        design.clockColor2 = jSONObject.optInt("clockColor2", design.clockColor2);
        boolean bl = jSONObject.has("font");
        Object object = "sys:0";
        boolean bl2 = false;
        if (bl) {
            design.font = FontSpec.fromJson(jSONObject.optJSONObject("font"), design.font);
        } else if (jSONObject.has("fontId")) {
            design.font = new FontSpec(jSONObject.optString("fontId", "sys:0"));
        } else if (jSONObject.has("clockFont")) {
            design.font = new FontSpec("sys:" + jSONObject.optInt("clockFont", 0));
        }
        design.clockSize = jSONObject.optInt("clockSize", design.clockSize);
        design.clockX = (float)jSONObject.optDouble("clockX", (double)design.clockX);
        design.clockY = (float)jSONObject.optDouble("clockY", (double)design.clockY);
        design.use24 = jSONObject.optBoolean("use24", design.use24);
        design.separator = Math.max(0, Math.min(SEPARATORS.length - 1, jSONObject.optInt("separator", design.separator)));
        design.letterSpacing = jSONObject.optInt("letterSpacing", design.letterSpacing);
        design.lineGap = jSONObject.optInt("lineGap", design.lineGap);
        design.clockOver = jSONObject.optBoolean("clockOver", design.clockOver);
        design.clockXf = Xf.fromJson(jSONObject.optJSONObject("clockXf"));
        design.textColor = jSONObject.optInt("textColor", design.textColor);
        design.dateShow = jSONObject.optBoolean("dateShow", design.dateShow);
        design.dateX = (float)jSONObject.optDouble("dateX", (double)design.dateX);
        design.dateY = (float)jSONObject.optDouble("dateY", (double)design.dateY);
        design.dateSize = jSONObject.optInt("dateSize", design.dateSize);
        design.dateFmt = Math.max(0, Math.min(DATE_FORMATS.length - 1, jSONObject.optInt("dateFmt", design.dateFmt)));
        Object object2 = jSONObject.optJSONObject("dateFont");
        if (design.font.id.startsWith("sys:")) {
            object = design.font.id;
        }
        design.dateFont = FontSpec.fromJson(object2, new FontSpec((String)object));
        design.dateXf = Xf.fromJson(jSONObject.optJSONObject("dateXf"));
        design.batShow = jSONObject.optBoolean("batShow", design.batShow);
        design.batX = (float)jSONObject.optDouble("batX", (double)design.batX);
        design.batY = (float)jSONObject.optDouble("batY", (double)design.batY);
        design.batSize = jSONObject.optInt("batSize", design.batSize);
        design.batXf = Xf.fromJson(jSONObject.optJSONObject("batXf"));
        design.parallax = jSONObject.optBoolean("parallax", design.parallax);
        design.parallaxStrength = jSONObject.optInt("parallaxStrength", design.parallaxStrength);
        design.parallaxText = jSONObject.optBoolean("parallaxText", design.parallaxText);
        design.particles = jSONObject.optInt("particles", design.particles);
        design.particleCount = jSONObject.optInt("particleCount", design.particleCount);
        design.pSpeed = jSONObject.optInt("pSpeed", design.pSpeed);
        design.pSize = jSONObject.optInt("pSize", design.pSize);
        design.pAlpha = jSONObject.optInt("pAlpha", design.pAlpha);
        design.pColor = jSONObject.optInt("pColor", design.pColor);
        design.autoColors = jSONObject.optBoolean("autoColors", design.autoColors);
        design.weatherAuto = jSONObject.optBoolean("weatherAuto", design.weatherAuto);
        design.weatherCity = jSONObject.optString("weatherCity", design.weatherCity);
        if (jSONObject.has("weatherLat") && jSONObject.has("weatherLon")) {
            design.weatherLat = jSONObject.optDouble("weatherLat", Double.NaN);
            design.weatherLon = jSONObject.optDouble("weatherLon", Double.NaN);
        }
        JSONArray jSONArray = jSONObject.optJSONArray("layers");
        object2 = "refreshMin";
        String string2 = "url";
        object = "";
        if (jSONArray != null) {
            for (n = 0; n < jSONArray.length() && n < 20; ++n) {
                JSONObject jSONObject2 = jSONArray.optJSONObject(n);
                if (jSONObject2 == null) continue;
                Layer layer = new Layer();
                layer.web = jSONObject2.optBoolean("web", bl2);
                layer.text = jSONObject2.optString("text", (String)object);
                layer.url = jSONObject2.optString(string2, (String)object);
                layer.refreshMin = Math.max(1, jSONObject2.optInt((String)object2, 30));
                layer.x = (float)jSONObject2.optDouble("x", 0.5);
                layer.y = (float)jSONObject2.optDouble("y", 0.62);
                layer.size = jSONObject2.optInt("size", 100);
                bl2 = false;
                layer.color = jSONObject2.optInt("color", 0);
                layer.font = FontSpec.fromJson(jSONObject2.optJSONObject("font"), new FontSpec());
                layer.xf = Xf.fromJson(jSONObject2.optJSONObject("xf"));
                design.layers.add(layer);
            }
        } else {
            if (!jSONObject.optString("text", "").isEmpty()) {
                object = new Layer();
                ((Layer)object).text = jSONObject.optString("text", "");
                ((Layer)object).x = (float)jSONObject.optDouble("textX", 0.5);
                ((Layer)object).y = (float)jSONObject.optDouble("textY", 0.62);
                ((Layer)object).size = jSONObject.optInt("textSize", 100);
                design.layers.add((Layer)object);
            }
            if (!jSONObject.optString("url", "").isEmpty()) {
                object = new Layer();
                ((Layer)object).web = true;
                ((Layer)object).url = jSONObject.optString("url", "");
                ((Layer)object).refreshMin = Math.max(1, jSONObject.optInt("refreshMin", 30));
                ((Layer)object).x = (float)jSONObject.optDouble("webX", 0.5);
                ((Layer)object).y = (float)jSONObject.optDouble("webY", 0.72);
                ((Layer)object).size = jSONObject.optInt("webSize", 100);
                design.layers.add((Layer)object);
            }
        }
        if ((jSONObject = jSONObject.optJSONArray("stickers")) != null) {
            for (n = 0; n < jSONObject.length() && n < 30; ++n) {
                object2 = jSONObject.optJSONObject(n);
                if (object2 == null) continue;
                object = new Sticker(object2.optString("s", "\u2b50"), (float)object2.optDouble("x", 0.5), (float)object2.optDouble("y", 0.5));
                ((Sticker)object).size = object2.optInt("size", 100);
                ((Sticker)object).xf = Xf.fromJson(object2.optJSONObject("xf"));
                design.stickers.add((Sticker)object);
            }
        }
        return design;
    }

    static Design load(Context context) {
        block3: {
            String string2 = Store.prefs(context).getString("design2", null);
            if (string2 == null) break block3;
            try {
                Object object = new JSONObject(string2);
                object = Design.fromJson((JSONObject)object);
                ((Design)object).attachRuntime(context);
                return object;
            }
            catch (Exception exception) {
                // empty catch block
            }
        }
        Design design = new Design();
        design.attachRuntime(context);
        return design;
    }

    static boolean matchesStyle(Design design, int n) {
        Design design2 = design.copy();
        Design.applyStyle(design2, n);
        boolean bl = design2.clockStyle == design.clockStyle && design2.outline == design.outline && design2.twoTone == design.twoTone && design2.gradText == design.gradText && (design2.clockStyle == 2 || design2.font.id.equals(design.font.id));
        return bl;
    }

    private static String shorten(String string2) {
        block0: {
            if (string2.length() <= 12) break block0;
            string2 = string2.substring(0, 12) + "\u2026";
        }
        return string2;
    }

    boolean animated() {
        boolean bl;
        int n = this.effKind();
        boolean bl2 = bl = true;
        if (n <= 0) {
            bl2 = this.bgMode == 1 && this.gradAnim ? bl : false;
        }
        return bl2;
    }

    void attachRuntime(Context context) {
        this.sys = SystemColors.auto(context);
        context = Store.prefs(context);
        long l = System.currentTimeMillis();
        long l2 = context.getLong("weatherAt", 0L);
        int n = -1;
        if (l - l2 < 14400000L) {
            n = context.getInt("weatherCode", -1);
        }
        this.weatherCode = n;
    }

    int clockCol() {
        int n = this.autoColors && this.sys != null ? this.sys[0] : this.clockColor;
        return n;
    }

    int clockCol2() {
        int n = this.autoColors && this.sys != null ? this.sys[1] : this.clockColor2;
        return n;
    }

    Design copy() {
        try {
            Design design = Design.fromJson(this.toJson()).copyRuntime(this);
            return design;
        }
        catch (Exception exception) {
            return new Design().copyRuntime(this);
        }
    }

    Design copyRuntime(Design design) {
        this.sys = design.sys;
        this.weatherCode = design.weatherCode;
        return this;
    }

    int effCount() {
        if (!this.weatherAuto) {
            return this.particleCount;
        }
        int n = this.weatherCode;
        if (n != 51 && n != 56) {
            if (n == 53) {
                return 45;
            }
            if (n != 55 && n != 57) {
                if (n != 61 && n != 80) {
                    if (n != 63 && n != 66 && n != 81) {
                        if (n != 65 && n != 67 && n != 82) {
                            if (n >= 95 && n <= 99) {
                                return 160;
                            }
                            if (n != 71 && n != 85) {
                                if (n != 73 && n != 77) {
                                    if (n != 75 && n != 86) {
                                        return 0;
                                    }
                                    return 140;
                                }
                                return 90;
                            }
                            return 50;
                        }
                        return 150;
                    }
                    return 100;
                }
                return 70;
            }
            return 60;
        }
        return 30;
    }

    int effKind() {
        if (!this.weatherAuto) {
            return this.particles;
        }
        int n = this.weatherCode;
        if (n >= 51 && n <= 67 || n >= 80 && n <= 82 || n >= 95 && n <= 99) {
            return 2;
        }
        if ((n < 71 || n > 77) && n != 85 && n != 86) {
            return 0;
        }
        return 1;
    }

    List<String[]> elements() {
        int n;
        ArrayList<String[]> arrayList = new ArrayList<String[]>();
        arrayList.add(new String[]{"clock", "\u0427\u0430\u0441\u044b"});
        arrayList.add(new String[]{"date", "\u0414\u0430\u0442\u0430"});
        arrayList.add(new String[]{"bat", "\u0411\u0430\u0442\u0430\u0440\u0435\u044f"});
        for (n = 0; n < this.layers.size(); ++n) {
            Object object = this.layers.get(n);
            object = ((Layer)object).web ? "\u0418\u0437 \u0438\u043d\u0442\u0435\u0440\u043d\u0435\u0442\u0430" : (((Layer)object).text.trim().isEmpty() ? "\u0422\u0435\u043a\u0441\u0442" : Design.shorten(((Layer)object).text.trim()));
            arrayList.add(new String[]{"tl" + n, object});
        }
        for (n = 0; n < this.stickers.size(); ++n) {
            arrayList.add(new String[]{"st" + n, this.stickers.get((int)n).s});
        }
        return arrayList;
    }

    FontSpec fontOf(String object) {
        Object var2_2 = null;
        if (object == null) {
            return null;
        }
        if (((String)object).equals("clock")) {
            return this.font;
        }
        if (((String)object).equals("date")) {
            return this.dateFont;
        }
        object = (object = this.layer((String)object)) == null ? var2_2 : ((Layer)object).font;
        return object;
    }

    int grad1Col() {
        int n = this.autoColors && this.sys != null ? this.sys[2] : this.grad1;
        return n;
    }

    int grad2Col() {
        int n = this.autoColors && this.sys != null ? this.sys[3] : this.grad2;
        return n;
    }

    boolean hasWebLayer() {
        Iterator<Layer> iterator2 = this.layers.iterator();
        while (iterator2.hasNext()) {
            if (!iterator2.next().web) continue;
            return true;
        }
        return false;
    }

    Layer layer(String object) {
        Object var3_3 = null;
        if (object != null && ((String)object).startsWith("tl")) {
            block4: {
                int n;
                try {
                    n = Integer.parseInt(((String)object).substring(2));
                    object = var3_3;
                    if (n < 0) break block4;
                    object = var3_3;
                }
                catch (NumberFormatException numberFormatException) {
                    return null;
                }
                if (n >= this.layers.size()) break block4;
                object = this.layers.get(n);
            }
            return object;
        }
        return null;
    }

    /*
     * Enabled aggressive block sorting
     */
    float[] pos(String object) {
        Object object2;
        int n;
        Object var3_2;
        block12: {
            var3_2 = null;
            if (object == null) {
                return null;
            }
            switch (((String)object).hashCode()) {
                case 94755854: {
                    if (!((String)object).equals("clock")) break;
                    n = 0;
                    break block12;
                }
                case 3076014: {
                    if (!((String)object).equals("date")) break;
                    n = 1;
                    break block12;
                }
                case 97301: {
                    if (!((String)object).equals("bat")) break;
                    n = 2;
                    break block12;
                }
            }
            n = -1;
        }
        switch (n) {
            default: {
                object2 = this.layer((String)object);
                if (object2 == null) break;
                return new float[]{((Layer)object2).x, ((Layer)object2).y};
            }
            case 2: {
                return new float[]{this.batX, this.batY};
            }
            case 1: {
                return new float[]{this.dateX, this.dateY};
            }
            case 0: {
                return new float[]{this.clockX, this.clockY};
            }
        }
        object2 = this.sticker((String)object);
        if (object2 == null) {
            return var3_2;
        }
        object = new float[2];
        object[0] = ((Sticker)object2).x;
        object[1] = ((Sticker)object2).y;
        return object;
    }

    void save(Context context) {
        try {
            Store.prefs(context).edit().putString("design2", this.toJson().toString()).apply();
        }
        catch (Exception exception) {
            // empty catch block
        }
    }

    /*
     * Enabled aggressive block sorting
     */
    void setPos(String object, float f, float f2) {
        int n;
        block11: {
            if (object == null) {
                return;
            }
            switch (((String)object).hashCode()) {
                case 94755854: {
                    if (!((String)object).equals("clock")) break;
                    n = 0;
                    break block11;
                }
                case 3076014: {
                    if (!((String)object).equals("date")) break;
                    n = 1;
                    break block11;
                }
                case 97301: {
                    if (!((String)object).equals("bat")) break;
                    n = 2;
                    break block11;
                }
            }
            n = -1;
        }
        switch (n) {
            default: {
                Layer layer = this.layer((String)object);
                if (layer == null) break;
                layer.x = f;
                layer.y = f2;
                return;
            }
            case 2: {
                this.batX = f;
                this.batY = f2;
                return;
            }
            case 1: {
                this.dateX = f;
                this.dateY = f2;
                return;
            }
            case 0: {
                this.clockX = f;
                this.clockY = f2;
                return;
            }
        }
        if ((object = this.sticker((String)object)) == null) return;
        ((Sticker)object).x = f;
        ((Sticker)object).y = f2;
    }

    /*
     * Enabled aggressive block sorting
     */
    void setSize(String object, int n) {
        int n2;
        block11: {
            if (object == null) {
                return;
            }
            int[] nArray = this.sizeRange((String)object);
            int n3 = 0;
            n2 = Math.max(nArray[0], Math.min(nArray[1], n));
            switch (((String)object).hashCode()) {
                case 94755854: {
                    if (!((String)object).equals("clock")) break;
                    n = n3;
                    break block11;
                }
                case 3076014: {
                    if (!((String)object).equals("date")) break;
                    n = 1;
                    break block11;
                }
                case 97301: {
                    if (!((String)object).equals("bat")) break;
                    n = 2;
                    break block11;
                }
            }
            n = -1;
        }
        switch (n) {
            default: {
                Layer layer = this.layer((String)object);
                if (layer == null) break;
                layer.size = n2;
                return;
            }
            case 2: {
                this.batSize = n2;
                return;
            }
            case 1: {
                this.dateSize = n2;
                return;
            }
            case 0: {
                this.clockSize = n2;
                return;
            }
        }
        if ((object = this.sticker((String)object)) == null) return;
        ((Sticker)object).size = n2;
    }

    /*
     * Enabled aggressive block sorting
     */
    int size(String object) {
        int n;
        int n2;
        block11: {
            n2 = 100;
            if (object == null) {
                return 100;
            }
            switch (((String)object).hashCode()) {
                case 94755854: {
                    if (!((String)object).equals("clock")) break;
                    n = 0;
                    break block11;
                }
                case 3076014: {
                    if (!((String)object).equals("date")) break;
                    n = 1;
                    break block11;
                }
                case 97301: {
                    if (!((String)object).equals("bat")) break;
                    n = 2;
                    break block11;
                }
            }
            n = -1;
        }
        switch (n) {
            default: {
                Layer layer = this.layer((String)object);
                if (layer == null) break;
                return layer.size;
            }
            case 2: {
                return this.batSize;
            }
            case 1: {
                return this.dateSize;
            }
            case 0: {
                return this.clockSize;
            }
        }
        if ((object = this.sticker((String)object)) != null) return ((Sticker)object).size;
        return n2;
    }

    int[] sizeRange(String string2) {
        if ("clock".equals(string2)) {
            return new int[]{40, 200};
        }
        if (string2 != null && string2.startsWith("st")) {
            return new int[]{30, 400};
        }
        return new int[]{40, 300};
    }

    Sticker sticker(String object) {
        Object var3_3 = null;
        if (object != null && ((String)object).startsWith("st")) {
            block4: {
                int n;
                try {
                    n = Integer.parseInt(((String)object).substring(2));
                    object = var3_3;
                    if (n < 0) break block4;
                    object = var3_3;
                }
                catch (NumberFormatException numberFormatException) {
                    return null;
                }
                if (n >= this.stickers.size()) break block4;
                object = this.stickers.get(n);
            }
            return object;
        }
        return null;
    }

    boolean stormy() {
        boolean bl = this.weatherAuto && this.weatherCode >= 95 && this.weatherCode <= 99;
        return bl;
    }

    int textCol() {
        int n = this.autoColors && this.sys != null ? this.sys[0] : this.textColor;
        return n;
    }

    JSONObject toJson() throws Exception {
        Layer layer2;
        JSONObject jSONObject = new JSONObject();
        jSONObject.put("bgMode", this.bgMode).put("grad1", this.grad1).put("grad2", this.grad2).put("gradAnim", this.gradAnim);
        jSONObject.put("dim", this.dim).put("hue", this.hue).put("sat", this.sat);
        jSONObject.put("clockShow", this.clockShow).put("clockStyle", this.clockStyle).put("outline", this.outline).put("twoTone", this.twoTone);
        jSONObject.put("gradText", this.gradText).put("gradAngle", this.gradAngle);
        jSONObject.put("stroke", this.stroke).put("strokeW", this.strokeW).put("strokeColor", this.strokeColor).put("shadow", this.shadow);
        jSONObject.put("clockColor", this.clockColor).put("clockColor2", this.clockColor2).put("font", (Object)this.font.toJson());
        jSONObject.put("clockSize", this.clockSize).put("clockX", (double)this.clockX).put("clockY", (double)this.clockY).put("use24", this.use24);
        jSONObject.put("separator", this.separator).put("letterSpacing", this.letterSpacing).put("lineGap", this.lineGap);
        jSONObject.put("clockOver", this.clockOver).put("clockXf", (Object)this.clockXf.toJson());
        jSONObject.put("textColor", this.textColor);
        jSONObject.put("dateShow", this.dateShow).put("dateX", (double)this.dateX).put("dateY", (double)this.dateY).put("dateSize", this.dateSize);
        jSONObject.put("dateFmt", this.dateFmt).put("dateFont", (Object)this.dateFont.toJson()).put("dateXf", (Object)this.dateXf.toJson());
        jSONObject.put("batShow", this.batShow).put("batX", (double)this.batX).put("batY", (double)this.batY).put("batSize", this.batSize);
        jSONObject.put("batXf", (Object)this.batXf.toJson());
        jSONObject.put("parallax", this.parallax).put("parallaxStrength", this.parallaxStrength).put("parallaxText", this.parallaxText);
        jSONObject.put("particles", this.particles).put("particleCount", this.particleCount);
        jSONObject.put("pSpeed", this.pSpeed).put("pSize", this.pSize).put("pAlpha", this.pAlpha).put("pColor", this.pColor);
        jSONObject.put("autoColors", this.autoColors).put("weatherAuto", this.weatherAuto).put("weatherCity", (Object)this.weatherCity);
        if (!Double.isNaN(this.weatherLat) && !Double.isNaN(this.weatherLon)) {
            jSONObject.put("weatherLat", this.weatherLat).put("weatherLon", this.weatherLon);
        }
        JSONArray object2 = new JSONArray();
        for (Layer layer2 : this.layers) {
            object2.put((Object)new JSONObject().put("web", layer2.web).put("text", (Object)layer2.text).put("url", (Object)layer2.url).put("refreshMin", layer2.refreshMin).put("x", (double)layer2.x).put("y", (double)layer2.y).put("size", layer2.size).put("color", layer2.color).put("font", (Object)layer2.font.toJson()).put("xf", (Object)layer2.xf.toJson()));
        }
        jSONObject.put("layers", (Object)object2);
        layer2 = new JSONArray();
        for (Sticker sticker : this.stickers) {
            layer2.put(new JSONObject().put("s", (Object)sticker.s).put("x", (double)sticker.x).put("y", (double)sticker.y).put("size", sticker.size).put("xf", (Object)sticker.xf.toJson()));
        }
        jSONObject.put("stickers", (Object)layer2);
        return jSONObject;
    }

    Layer webLayer() {
        for (Layer layer : this.layers) {
            if (!layer.web || layer.url.trim().isEmpty()) continue;
            return layer;
        }
        return null;
    }

    /*
     * Enabled aggressive block sorting
     */
    Xf xf(String object) {
        int n;
        Object var3_2;
        block11: {
            var3_2 = null;
            if (object == null) {
                return null;
            }
            switch (((String)object).hashCode()) {
                case 94755854: {
                    if (!((String)object).equals("clock")) break;
                    n = 0;
                    break block11;
                }
                case 3076014: {
                    if (!((String)object).equals("date")) break;
                    n = 1;
                    break block11;
                }
                case 97301: {
                    if (!((String)object).equals("bat")) break;
                    n = 2;
                    break block11;
                }
            }
            n = -1;
        }
        switch (n) {
            default: {
                Layer layer = this.layer((String)object);
                if (layer == null) break;
                return layer.xf;
            }
            case 2: {
                return this.batXf;
            }
            case 1: {
                return this.dateXf;
            }
            case 0: {
                return this.clockXf;
            }
        }
        if ((object = this.sticker((String)object)) != null) return ((Sticker)object).xf;
        return var3_2;
    }

    static final class FontSpec {
        int cnt = -9999;
        int hgt = -9999;
        int hstem = -9999;
        String id = "sys:0";
        int slnt = -9999;
        int stem = -9999;
        int wdth = -9999;
        int wght = -9999;

        FontSpec() {
        }

        FontSpec(String string2) {
            this.id = string2;
        }

        private static void add(StringBuilder stringBuilder, String string2, int n) {
            if (n == -9999) {
                return;
            }
            if (stringBuilder.length() > 0) {
                stringBuilder.append(", ");
            }
            stringBuilder.append('\'').append(string2).append("' ").append(n);
        }

        static FontSpec fromJson(JSONObject jSONObject, FontSpec fontSpec) {
            if (jSONObject == null) {
                return fontSpec;
            }
            fontSpec = new FontSpec(jSONObject.optString("id", fontSpec.id));
            fontSpec.wght = jSONObject.optInt("wght", -9999);
            fontSpec.wdth = jSONObject.optInt("wdth", -9999);
            fontSpec.slnt = jSONObject.optInt("slnt", -9999);
            fontSpec.hgt = jSONObject.optInt("hgt", -9999);
            fontSpec.stem = jSONObject.optInt("stem", -9999);
            fontSpec.hstem = jSONObject.optInt("hstem", -9999);
            fontSpec.cnt = jSONObject.optInt("cnt", -9999);
            return fontSpec;
        }

        FontSpec copy() {
            FontSpec fontSpec = new FontSpec(this.id);
            fontSpec.wght = this.wght;
            fontSpec.wdth = this.wdth;
            fontSpec.slnt = this.slnt;
            fontSpec.hgt = this.hgt;
            fontSpec.stem = this.stem;
            fontSpec.hstem = this.hstem;
            fontSpec.cnt = this.cnt;
            return fontSpec;
        }

        boolean hasAxes() {
            boolean bl = this.wght != -9999 || this.wdth != -9999 || this.slnt != -9999 || this.hgt != -9999 || this.stem != -9999 || this.hstem != -9999 || this.cnt != -9999;
            return bl;
        }

        void resetAxes() {
            this.cnt = -9999;
            this.hstem = -9999;
            this.stem = -9999;
            this.hgt = -9999;
            this.slnt = -9999;
            this.wdth = -9999;
            this.wght = -9999;
        }

        JSONObject toJson() throws Exception {
            JSONObject jSONObject = new JSONObject().put("id", (Object)this.id);
            if (this.wght != -9999) {
                jSONObject.put("wght", this.wght);
            }
            if (this.wdth != -9999) {
                jSONObject.put("wdth", this.wdth);
            }
            if (this.slnt != -9999) {
                jSONObject.put("slnt", this.slnt);
            }
            if (this.hgt != -9999) {
                jSONObject.put("hgt", this.hgt);
            }
            if (this.stem != -9999) {
                jSONObject.put("stem", this.stem);
            }
            if (this.hstem != -9999) {
                jSONObject.put("hstem", this.hstem);
            }
            if (this.cnt != -9999) {
                jSONObject.put("cnt", this.cnt);
            }
            return jSONObject;
        }

        String variation() {
            StringBuilder stringBuilder = new StringBuilder();
            FontSpec.add(stringBuilder, "wght", this.wght);
            FontSpec.add(stringBuilder, "wdth", this.wdth);
            FontSpec.add(stringBuilder, "slnt", this.slnt);
            FontSpec.add(stringBuilder, "YTFI", this.hgt);
            FontSpec.add(stringBuilder, "XOPQ", this.stem);
            FontSpec.add(stringBuilder, "YOPQ", this.hstem);
            FontSpec.add(stringBuilder, "XTRA", this.cnt);
            return stringBuilder.toString();
        }
    }

    static final class Layer {
        int color = 0;
        FontSpec font = new FontSpec();
        int refreshMin = 30;
        int size = 100;
        String text = "";
        String url = "";
        boolean web;
        float x = 0.5f;
        Xf xf = new Xf();
        float y = 0.62f;

        Layer() {
        }

        Layer copy() {
            Layer layer = new Layer();
            layer.web = this.web;
            layer.text = this.text;
            layer.url = this.url;
            layer.refreshMin = this.refreshMin;
            layer.x = Math.min(0.96f, this.x + 0.04f);
            layer.y = Math.min(0.97f, this.y + 0.04f);
            layer.size = this.size;
            layer.color = this.color;
            layer.font = this.font.copy();
            layer.xf = this.xf.copy();
            return layer;
        }
    }

    static final class Sticker {
        String s;
        int size = 100;
        float x;
        Xf xf = new Xf();
        float y;

        Sticker(String string2, float f, float f2) {
            this.s = string2;
            this.x = f;
            this.y = f2;
        }
    }

    static final class Xf {
        int rot;
        int rx;
        int ry;
        int skew;
        int sx = 100;
        int sy = 100;

        Xf() {
        }

        static Xf fromJson(JSONObject jSONObject) {
            Xf xf = new Xf();
            if (jSONObject == null) {
                return xf;
            }
            xf.rot = jSONObject.optInt("rot", 0);
            xf.rx = jSONObject.optInt("rx", 0);
            xf.ry = jSONObject.optInt("ry", 0);
            xf.skew = jSONObject.optInt("skew", 0);
            xf.sx = jSONObject.optInt("sx", 100);
            xf.sy = jSONObject.optInt("sy", 100);
            return xf;
        }

        Xf copy() {
            Xf xf = new Xf();
            xf.rot = this.rot;
            xf.rx = this.rx;
            xf.ry = this.ry;
            xf.skew = this.skew;
            xf.sx = this.sx;
            xf.sy = this.sy;
            return xf;
        }

        boolean identity() {
            boolean bl = this.rot == 0 && this.rx == 0 && this.ry == 0 && this.skew == 0 && this.sx == 100 && this.sy == 100;
            return bl;
        }

        void reset() {
            this.skew = 0;
            this.ry = 0;
            this.rx = 0;
            this.rot = 0;
            this.sy = 100;
            this.sx = 100;
        }

        JSONObject toJson() throws Exception {
            return new JSONObject().put("rot", this.rot).put("rx", this.rx).put("ry", this.ry).put("skew", this.skew).put("sx", this.sx).put("sy", this.sy);
        }
    }
}

