/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.app.Activity
 *  android.app.AlertDialog$Builder
 *  android.content.ComponentName
 *  android.content.Context
 *  android.content.DialogInterface
 *  android.content.DialogInterface$OnClickListener
 *  android.content.Intent
 *  android.content.SharedPreferences
 *  android.graphics.drawable.Drawable
 *  android.net.Uri
 *  android.os.Build$VERSION
 *  android.os.Bundle
 *  android.os.Parcelable
 *  android.text.Editable
 *  android.text.TextUtils$TruncateAt
 *  android.text.TextWatcher
 *  android.view.View
 *  android.view.View$OnClickListener
 *  android.view.ViewGroup$LayoutParams
 *  android.widget.EditText
 *  android.widget.HorizontalScrollView
 *  android.widget.LinearLayout
 *  android.widget.LinearLayout$LayoutParams
 *  android.widget.ScrollView
 *  android.widget.TextView
 *  android.widget.Toast
 *  org.json.JSONArray
 *  org.json.JSONObject
 */
package com.lockscreen.customplus;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ComponentName;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.drawable.Drawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Parcelable;
import android.text.Editable;
import android.text.TextUtils;
import android.text.TextWatcher;
import android.view.View;
import android.view.ViewGroup;
import android.widget.EditText;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;
import com.lockscreen.customplus.ClockWallpaperService;
import com.lockscreen.customplus.Design;
import com.lockscreen.customplus.EditorActivity;
import com.lockscreen.customplus.Fonts;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda0;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda1;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda10;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda100;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda101;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda102;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda103;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda104;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda105;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda106;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda11;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda12;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda13;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda14;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda15;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda16;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda17;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda18;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda19;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda2;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda20;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda21;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda22;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda23;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda24;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda25;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda26;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda27;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda28;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda29;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda3;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda30;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda31;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda32;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda33;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda34;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda35;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda36;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda37;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda38;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda39;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda4;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda40;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda41;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda42;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda43;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda44;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda45;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda46;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda47;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda48;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda49;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda5;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda50;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda51;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda52;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda53;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda54;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda55;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda56;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda57;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda58;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda59;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda6;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda60;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda61;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda62;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda63;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda64;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda65;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda66;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda67;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda68;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda69;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda7;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda70;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda71;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda72;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda73;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda74;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda75;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda76;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda77;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda78;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda79;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda8;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda80;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda81;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda82;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda83;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda84;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda85;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda86;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda87;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda88;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda89;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda9;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda90;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda91;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda92;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda93;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda94;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda95;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda96;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda97;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda98;
import com.lockscreen.customplus.MainActivity$$ExternalSyntheticLambda99;
import com.lockscreen.customplus.PreviewView;
import com.lockscreen.customplus.Store;
import com.lockscreen.customplus.SystemColors;
import com.lockscreen.customplus.Ui;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.function.IntConsumer;
import org.json.JSONArray;
import org.json.JSONObject;

public class MainActivity
extends Activity {
    private static final String[] COLOR_TARGETS;
    private static final String[] EMOJI;
    private static final Set<String> FLEX_FONTS;
    private static final int[] GRADIENT_COLORS;
    private static final int[] PALETTE;
    private static final String[] PARTICLE_NAMES;
    private static final int REQ_BG = 1;
    private static final int REQ_CUT = 2;
    private static final int REQ_FONT = 3;
    private static final int SUB = -6512986;
    private static final int TAB_COLORS = 2;
    private static final int TAB_ELEMENTS = 3;
    private static final String[] TAB_ICONS;
    private static final int TAB_MINE = 5;
    private static final String[] TAB_NAMES;
    private static final int TEXT = -723722;
    private static final Set<String> WEIGHT_FONTS;
    private int colorTarget = 0;
    private LinearLayout content;
    private Design design;
    private TextView fetchResult;
    private boolean fromEditor;
    private final List<PreviewView> minis = new ArrayList<PreviewView>();
    private LinearLayout nav;
    private TextView photoStatus;
    private PreviewView preview;
    private ScrollView scroll;
    private String selectedId = "clock";
    private SharedPreferences sp;
    private int tab = 0;
    private Ui ui;
    private TextView weatherResult;

    public static /* synthetic */ void $r8$lambda$_1ivBvhKsxY7mOLUXBc_qzA_OPw(MainActivity mainActivity, String string2) {
        mainActivity.lambda$testFetch$72(string2);
    }

    public static /* synthetic */ void $r8$lambda$_vJzp79ar3cnj5BJljj5jTYxf1A(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildClockTab$25(n);
    }

    public static /* synthetic */ void $r8$lambda$0FWuBoINMQHmtYoMaCaWSfSmc8w(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildEffectsTab$76(bl);
    }

    public static /* synthetic */ void $r8$lambda$0MRykG6Jrd8zmiQCnvXIdazKvNQ(MainActivity mainActivity, String string2) {
        mainActivity.lambda$findWeather$89(string2);
    }

    public static /* synthetic */ void $r8$lambda$0ffbAOgIPr4AIig_7gPxzbFqmbc(MainActivity mainActivity, String string2, int n) {
        mainActivity.lambda$buildElementsTab$50(string2, n);
    }

    public static /* synthetic */ void $r8$lambda$0y8URtek4DGL6laQk94mCW4yudc(MainActivity mainActivity) {
        mainActivity.lambda$buildElementsTab$64();
    }

    public static /* synthetic */ void $r8$lambda$1kaF_HMoQycE3vtVYCk_fyGvRmI(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildEffectsTab$81(n);
    }

    public static /* synthetic */ void $r8$lambda$1kfRXIwgYHTckLJ7hrKHRFglJEQ(MainActivity mainActivity, Design.FontSpec fontSpec) {
        mainActivity.lambda$fontSection$42(fontSpec);
    }

    public static /* synthetic */ void $r8$lambda$2tGax4cVYirTFP5MqnuBxURf3Mg(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildBackgroundTab$12(bl);
    }

    public static /* synthetic */ void $r8$lambda$3fUPHDXjbfc7C_PTJbUVqaegzJM(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildClockTab$20(n);
    }

    public static /* synthetic */ void $r8$lambda$4XqBz2zqBIMIuvYzSZWeOartBoc(MainActivity mainActivity, String string2) {
        mainActivity.lambda$onActivityResult$95(string2);
    }

    public static /* synthetic */ void $r8$lambda$4qxhzyxFt6_GjCZBziYumjWbhHM(MainActivity mainActivity, String string2, String string3) {
        mainActivity.lambda$onActivityResult$96(string2, string3);
    }

    public static /* synthetic */ void $r8$lambda$74Gw_Ta8laZ1C5c8_kMtl2hh83Y(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildElementsTab$59(bl);
    }

    public static /* synthetic */ void $r8$lambda$7nvwoeYPdA7ACxqSp8xaDYKvHVY(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildColorsTab$48(n);
    }

    public static /* synthetic */ void $r8$lambda$83KeAaviNngueMNEzsM7ze851Sk(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildClockTab$18(bl);
    }

    public static /* synthetic */ void $r8$lambda$8habQAenLkmFLtJFbDuvq4qnzdM(MainActivity mainActivity, String string2, View view) {
        mainActivity.lambda$emojiChips$71(string2, view);
    }

    public static /* synthetic */ void $r8$lambda$8t4C_kIoKCHI2dEroKEOZiv_CCY(MainActivity mainActivity) {
        mainActivity.runAutoCut();
    }

    public static /* synthetic */ void $r8$lambda$B_AxSXytWtL7R4ZXMrOcGT_l1HM(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildClockTab$27(bl);
    }

    public static /* synthetic */ void $r8$lambda$BXY_VdrMls5Ejpfo2ABkDqSUyOg(MainActivity mainActivity) {
        mainActivity.lambda$buildBackgroundTab$5();
    }

    public static /* synthetic */ void $r8$lambda$B_kQt_d3PAO2NvTG2ohCzBPffJw(MainActivity mainActivity) {
        mainActivity.lambda$buildEffectsTab$86();
    }

    public static /* synthetic */ void $r8$lambda$Bry0PdY9sExgQ3SUMFn7vgX_R44(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildEffectsTab$82(n);
    }

    public static /* synthetic */ void $r8$lambda$CA04bMAXTEMrvDgSaYNZeN9U_qc(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildClockTab$24(bl);
    }

    public static /* synthetic */ void $r8$lambda$CUJdQdA0jPQjZoBlAcLopGL_eVI(MainActivity mainActivity, List list, int n, Design.FontSpec fontSpec, Fonts.Entry entry, View view) {
        mainActivity.lambda$fontChips$46(list, n, fontSpec, entry, view);
    }

    public static /* synthetic */ void $r8$lambda$DUwB5dHNuH3md82xMMprOLztoYA(MainActivity mainActivity, Design.Xf xf, int n) {
        mainActivity.lambda$buildElementsTab$55(xf, n);
    }

    public static /* synthetic */ void $r8$lambda$F2OMSWHssbjRJgYUXJZ11_jEDQg(MainActivity mainActivity, Design.Xf xf, int n) {
        mainActivity.lambda$buildElementsTab$52(xf, n);
    }

    public static /* synthetic */ void $r8$lambda$FL7_2K3TUSpi1WdC1zUhqKbXU8w(MainActivity mainActivity, int n, int n2) {
        mainActivity.lambda$nudgeCut$17(n, n2);
    }

    public static /* synthetic */ void $r8$lambda$H1hgo8bv6IL_JW5bP4u1RTp0_zo(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildClockTab$21(n);
    }

    public static /* synthetic */ void $r8$lambda$HBIGRrihT2itn7TyJ9QPKgmONVA(MainActivity mainActivity, Design.FontSpec fontSpec) {
        mainActivity.lambda$fontSection$44(fontSpec);
    }

    public static /* synthetic */ void $r8$lambda$HrLUENdl1tSDeZT8frsVoMPQHog(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildMineTab$91(n);
    }

    public static /* synthetic */ void $r8$lambda$HxuJy2xlLPfxSPGvuKyi1_0AlQo(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildClockTab$26(n);
    }

    public static /* synthetic */ void $r8$lambda$I9aybmK1x45key7d7eaheIxNryE(MainActivity mainActivity) {
        mainActivity.testFetch();
    }

    public static /* synthetic */ void $r8$lambda$IOns9EcC21kmu3kq3tzIgwtqz_o(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildClockTab$22(n);
    }

    public static /* synthetic */ void $r8$lambda$IxFNlI34dWPL8f8m3fWv9LIFGns(MainActivity mainActivity) {
        mainActivity.lambda$fontSection$43();
    }

    public static /* synthetic */ void $r8$lambda$IzwvKZegJq9qzZWUl9ti776R8gY(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildClockTab$28(n);
    }

    public static /* synthetic */ void $r8$lambda$JiTQPI6W10cg7tSUVbiJuOi78b4(MainActivity mainActivity) {
        mainActivity.applyWallpaper();
    }

    public static /* synthetic */ void $r8$lambda$JvDtqOIfvhZePlM0VcOYEN9WKSQ(MainActivity mainActivity, int n, View view) {
        mainActivity.lambda$buildNav$0(n, view);
    }

    public static /* synthetic */ void $r8$lambda$KTevqQuNwbUDVpZeFge8m6gwA6M(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildElementsTab$61(bl);
    }

    public static /* synthetic */ void $r8$lambda$Kco9662pZ9TiXwr8e7XlVhR7buM(MainActivity mainActivity, String string2) {
        mainActivity.lambda$findWeather$88(string2);
    }

    public static /* synthetic */ void $r8$lambda$KsnxXrvwDakrC4la9mIcslt4ZDs(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildBackgroundTab$13(n);
    }

    public static /* synthetic */ void $r8$lambda$L8SOqbNQGsi5cJ51X1OTM0yRxd4(MainActivity mainActivity) {
        mainActivity.savePresetDialog();
    }

    public static /* synthetic */ void $r8$lambda$MIypXp_6xdb_3akAus_WlAcs1fA(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildElementsTab$58(bl);
    }

    public static /* synthetic */ void $r8$lambda$MzJdPHoRFRxQxEd3babRWpzUOXY(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildClockTab$31(n);
    }

    public static /* synthetic */ void $r8$lambda$NxLcDEnK8WwGST1nb0QktQJ_vlQ(MainActivity mainActivity, Design.Xf xf, int n) {
        mainActivity.lambda$buildElementsTab$51(xf, n);
    }

    public static /* synthetic */ void $r8$lambda$OhjwTizvHsnFmyNH95RaNZ5HpzY(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildEffectsTab$74(bl);
    }

    public static /* synthetic */ void $r8$lambda$QVCvGzVTgc4ng9UPKG1_SXFoTPc(MainActivity mainActivity, EditText editText) {
        mainActivity.lambda$buildEffectsTab$78(editText);
    }

    public static /* synthetic */ void $r8$lambda$QXl5auZLYSBUyuuyNetU89NlRe8(MainActivity mainActivity, DialogInterface dialogInterface, int n) {
        mainActivity.lambda$buildMineTab$92(dialogInterface, n);
    }

    public static /* synthetic */ void $r8$lambda$Q_FTikpDtqXUoLj_PzpNS3eSDXU(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildElementsTab$60(n);
    }

    public static /* synthetic */ void $r8$lambda$RMxrFRbXSK28LLwpojS_oDK2010(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildEffectsTab$84(n);
    }

    public static /* synthetic */ void $r8$lambda$RP1hU3PJzsfpuoq01_BR2vytvTw(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildElementsTab$66(n);
    }

    public static /* synthetic */ void $r8$lambda$ShXYnV6JtEQVZCalo2t8A4obULY(MainActivity mainActivity, Design.Xf xf, int n) {
        mainActivity.lambda$buildElementsTab$56(xf, n);
    }

    public static /* synthetic */ void $r8$lambda$TFoit9_fi8LJSkDBYd5KxpMSgEw(MainActivity mainActivity, int n, View view) {
        mainActivity.lambda$styleCard$33(n, view);
    }

    public static /* synthetic */ void $r8$lambda$UyleEMZUEnhNiRJx6abHHWOcQeI(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildBackgroundTab$14(n);
    }

    public static /* synthetic */ void $r8$lambda$VeORosI0233ihsRxg_e0ERMyVM0(MainActivity mainActivity, EditText editText, DialogInterface dialogInterface, int n) {
        mainActivity.lambda$savePresetDialog$94(editText, dialogInterface, n);
    }

    public static /* synthetic */ void $r8$lambda$VjKSNcanGRaRO1K_55PGcwOB0Eo(MainActivity mainActivity, Design.Layer layer) {
        mainActivity.lambda$layerCard$70(layer);
    }

    public static /* synthetic */ void $r8$lambda$Vo6hOXx8H7F73M1gqm9KkR9f_fE(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildBackgroundTab$11(n);
    }

    public static /* synthetic */ void $r8$lambda$WQKkpE5Ob3mxnRLtv2LzHs1bPZs(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildBackgroundTab$15(n);
    }

    public static /* synthetic */ void $r8$lambda$Y8QhryjeLkWmFUfVxFBam6NMN_A(MainActivity mainActivity) {
        mainActivity.lambda$buildBackgroundTab$4();
    }

    public static /* synthetic */ void $r8$lambda$Z54OxVBs1Xr5cRDRw1oyYBebEI0(MainActivity mainActivity) {
        mainActivity.lambda$buildBackgroundTab$3();
    }

    public static /* synthetic */ void $r8$lambda$ZAist107_5lPzlrrG8b2Zw3shK4(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildClockTab$32(n);
    }

    public static /* synthetic */ void $r8$lambda$ZOkS34m2hqnc1Y8LSpjUH7qE3T0(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildEffectsTab$77(bl);
    }

    public static /* synthetic */ void $r8$lambda$_AjtqSTze7t6X3P0IoztSxFPfv8(MainActivity mainActivity, Design.Xf xf, int n) {
        mainActivity.lambda$buildElementsTab$54(xf, n);
    }

    public static /* synthetic */ void $r8$lambda$_OI8POhjDwVI7ksRDoQsM9riTwg(MainActivity mainActivity, String string2, String string3) {
        mainActivity.lambda$runAutoCut$98(string2, string3);
    }

    public static /* synthetic */ void $r8$lambda$_dhWIORVZ18_3xRlD5TvGF53vbQ(MainActivity mainActivity, int n) {
        mainActivity.lambda$rebuild$1(n);
    }

    public static /* synthetic */ void $r8$lambda$aAVTn9cRcaBET8KRGm4XgFfdCho(MainActivity mainActivity) {
        mainActivity.openEditor();
    }

    public static /* synthetic */ void $r8$lambda$bYZcjuMPPNreh6loZnEBoPbm6x0(MainActivity mainActivity) {
        mainActivity.lambda$runAutoCut$99();
    }

    public static /* synthetic */ void $r8$lambda$bdoTR_ZlZfblewsIPFfvRwuvkSQ(MainActivity mainActivity) {
        mainActivity.refreshAfterImages();
    }

    public static /* synthetic */ void $r8$lambda$dQbiKR0OMFN02UQ37IAzaAw_tSk(MainActivity mainActivity, Design.Layer layer) {
        mainActivity.lambda$layerCard$69(layer);
    }

    public static /* synthetic */ void $r8$lambda$dk79mEkBurjeVx2jsS1KWH_bCeQ(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildEffectsTab$75(n);
    }

    public static /* synthetic */ void $r8$lambda$eg4HNAPhdtNyU9RRGvYr8S9fHVY(MainActivity mainActivity, String string2) {
        mainActivity.lambda$buildElementsTab$63(string2);
    }

    public static /* synthetic */ void $r8$lambda$fL3SA1boJShQA5IefrEER_AjIMQ(MainActivity mainActivity, String string2) {
        mainActivity.lambda$testFetch$73(string2);
    }

    public static /* synthetic */ void $r8$lambda$i6SY5ezAlJSoBK46rkwCJ8zjU8Q(MainActivity mainActivity, JSONObject jSONObject) {
        mainActivity.lambda$buildMineTab$90(jSONObject);
    }

    public static /* synthetic */ void $r8$lambda$iH6KJrzhL_ekZZ2Qf4wc1Nu3TKo(MainActivity mainActivity, List list, int n) {
        mainActivity.lambda$buildElementsTab$49(list, n);
    }

    public static /* synthetic */ void $r8$lambda$jtYpvhJlqj5LGqwDY7Kes3v0cto(MainActivity mainActivity, int n) {
        mainActivity.applySystemColor(n);
    }

    public static /* synthetic */ void $r8$lambda$ko6KeUWpJDUgOnAzwfrSO1tDnRo(MainActivity mainActivity, Store.Place place) {
        mainActivity.lambda$findWeather$87(place);
    }

    public static /* synthetic */ void $r8$lambda$lkkhh7cq7LwprpyN8pTP5JGjJFA(MainActivity mainActivity, Design.Layer layer, int n) {
        mainActivity.lambda$layerCard$67(layer, n);
    }

    public static /* synthetic */ void $r8$lambda$n72CmM_Wo_cnMCl_JIYj5lGIemI(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildClockTab$23(bl);
    }

    public static /* synthetic */ void $r8$lambda$nJ4HN24GuMa7_0CjgyZhVm34Wjk(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildEffectsTab$85(n);
    }

    public static /* synthetic */ void $r8$lambda$oKx9ufjCB4E7LCpUuCqSu1AXeXs(MainActivity mainActivity, String string2) {
        mainActivity.lambda$buildElementsTab$62(string2);
    }

    public static /* synthetic */ void $r8$lambda$p1bNyVVjfRpPCY5g6ctxRjcUM9w(MainActivity mainActivity, int n, Uri uri) {
        mainActivity.lambda$onActivityResult$97(n, uri);
    }

    public static /* synthetic */ void $r8$lambda$pGJ6tbav9ZCto1OVyFlelp2zCzI(MainActivity mainActivity) {
        mainActivity.lambda$buildBackgroundTab$6();
    }

    public static /* synthetic */ void $r8$lambda$pK2hBb5LPQqMmIQX7Ttw_yp6tD4(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildEffectsTab$83(n);
    }

    public static /* synthetic */ void $r8$lambda$pX8WWqGbNvWKqmhRqJu3A3YWSHo(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildEffectsTab$80(n);
    }

    public static /* synthetic */ void $r8$lambda$p_VZJQsF_kB3LQis_5VJX_Lx4XI(MainActivity mainActivity) {
        mainActivity.lambda$buildBackgroundTab$2();
    }

    public static /* synthetic */ void $r8$lambda$qR9yfNqcsc0r4v9n7OsR4LEwErQ(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildBackgroundTab$9(n);
    }

    public static /* synthetic */ void $r8$lambda$qYPuDv_yMBbfbliWblerHwOTTwM(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildBackgroundTab$10(n);
    }

    public static /* synthetic */ void $r8$lambda$qk6ZnQ9Gj199KACek2S0VaTdTd0(MainActivity mainActivity, Design.Xf xf, int n) {
        mainActivity.lambda$buildElementsTab$53(xf, n);
    }

    public static /* synthetic */ void $r8$lambda$r3kMmm0wAVcXfwt1ieyVKz07j44(MainActivity mainActivity) {
        mainActivity.lambda$buildBackgroundTab$7();
    }

    public static /* synthetic */ void $r8$lambda$rfW9SVxF0gME_cZ1N35bLPlfr_Y(MainActivity mainActivity) {
        mainActivity.lambda$buildMineTab$93();
    }

    public static /* synthetic */ void $r8$lambda$sApzknpf3983klB_c335h0v1LTU(MainActivity mainActivity, Design.Layer layer) {
        mainActivity.lambda$layerCard$68(layer);
    }

    public static /* synthetic */ void $r8$lambda$tBtSTUXcj8ceOkO31zg_uyuF3_c(MainActivity mainActivity) {
        mainActivity.lambda$buildElementsTab$65();
    }

    public static /* synthetic */ void $r8$lambda$tct980WLh7Et50mi6C0bk699tqg(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildClockTab$29(bl);
    }

    public static /* synthetic */ void $r8$lambda$tek_pNC5_nAywflRPLquVyt1a7g(MainActivity mainActivity, Design.Xf xf) {
        mainActivity.lambda$buildElementsTab$57(xf);
    }

    public static /* synthetic */ void $r8$lambda$vF0ftLvp5VMvyAntNo6LdaToH88(MainActivity mainActivity, Design.FontSpec fontSpec) {
        mainActivity.lambda$fontSection$34(fontSpec);
    }

    public static /* synthetic */ void $r8$lambda$vaHigzMV2aSZ87LIh66gHWpyL6k(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildClockTab$30(bl);
    }

    public static /* synthetic */ void $r8$lambda$wEMpqdJS1_C8Dk8G6jFC6Am5K0s(MainActivity mainActivity, int[] nArray, int n) {
        mainActivity.lambda$buildEffectsTab$79(nArray, n);
    }

    public static /* synthetic */ void $r8$lambda$wdM3QA8FfxPNKf7L87YQBfz_xbM(MainActivity mainActivity) {
        mainActivity.lambda$buildBackgroundTab$8();
    }

    public static /* synthetic */ void $r8$lambda$xBvLE9skTWw1VS1ely46U_y61r0(MainActivity mainActivity) {
        mainActivity.lambda$buildBackgroundTab$16();
    }

    public static /* synthetic */ void $r8$lambda$yWZn_DpAXyWjq_1i4ygeGN1ollk(MainActivity mainActivity, IntConsumer intConsumer, int n) {
        mainActivity.lambda$axis$45(intConsumer, n);
    }

    public static /* synthetic */ void $r8$lambda$z1KXhN8Pygzaew4a1jTN29QTBpo(MainActivity mainActivity, int n) {
        mainActivity.lambda$buildClockTab$19(n);
    }

    public static /* synthetic */ void $r8$lambda$zU9dgpTbcz_4xZOt4g4LOwDQFbA(MainActivity mainActivity, Boolean bl) {
        mainActivity.lambda$buildColorsTab$47(bl);
    }

    static {
        TAB_ICONS = new String[]{"\ud83d\uddbc", "\ud83d\udd52", "\ud83c\udfa8", "\ud83e\udde9", "\u2728", "\ud83d\udcbe"};
        TAB_NAMES = new String[]{"\u0424\u043e\u043d", "\u0427\u0430\u0441\u044b", "\u0426\u0432\u0435\u0442\u0430", "\u042d\u043b\u0435\u043c\u0435\u043d\u0442\u044b", "\u042d\u0444\u0444\u0435\u043a\u0442\u044b", "\u041c\u043e\u0438"};
        COLOR_TARGETS = new String[]{"\u0427\u0430\u0441\u044b", "\u041c\u0438\u043d\u0443\u0442\u044b", "\u041d\u0430\u0434\u043f\u0438\u0441\u0438", "\u0413\u0440\u0430\u0434\u0438\u0435\u043d\u0442 1", "\u0413\u0440\u0430\u0434\u0438\u0435\u043d\u0442 2"};
        PALETTE = new int[]{-1, -5317, -16718337, -49023, -9834322, -28416, -1754827, -16777216};
        GRADIENT_COLORS = new int[]{-14997707, -11830879, -7459358, -44753, -15623794, -13045891, -244117, -12624133, -551138, -16777216};
        EMOJI = new String[]{"\ud83d\ude00", "\ud83d\ude0e", "\ud83d\udd25", "\u2764\ufe0f", "\u2b50", "\ud83c\udf19", "\u26a1", "\ud83c\udfb5", "\ud83d\ude80", "\ud83c\udf38", "\ud83d\udc51", "\ud83d\udc8e"};
        PARTICLE_NAMES = new String[]{"\u041d\u0435\u0442", "\u0421\u043d\u0435\u0433", "\u0414\u043e\u0436\u0434\u044c", "\u0417\u0432\u0451\u0437\u0434\u044b", "\u041f\u0443\u0437\u044b\u0440\u0438", "\u0421\u0430\u043a\u0443\u0440\u0430"};
        FLEX_FONTS = new HashSet<String>(Arrays.asList("asset:RobotoFlex.ttf"));
        WEIGHT_FONTS = new HashSet<String>(Arrays.asList("asset:Oswald.ttf", "asset:Orbitron.ttf", "asset:Comfortaa.ttf", "asset:PlayfairDisplay.ttf"));
    }

    private void addLayer(boolean bl) {
        if (this.design.layers.size() >= 20) {
            this.toast("\u0421\u043b\u0438\u0448\u043a\u043e\u043c \u043c\u043d\u043e\u0433\u043e \u043d\u0430\u0434\u043f\u0438\u0441\u0435\u0439");
            return;
        }
        Design.Layer layer = new Design.Layer();
        layer.web = bl;
        layer.y = Math.min(0.9f, (float)this.design.layers.size() * 0.06f + 0.62f);
        if (!bl) {
            layer.text = "\u041d\u043e\u0432\u044b\u0439 \u0442\u0435\u043a\u0441\u0442";
        }
        this.design.layers.add(layer);
        this.select("tl" + (this.design.layers.size() - 1));
        this.commit();
        this.rebuild();
    }

    private void applySystemColor(int n) {
        switch (this.colorTarget) {
            default: {
                this.design.bgMode = 1;
                this.design.grad2 = n;
                break;
            }
            case 3: {
                this.design.bgMode = 1;
                this.design.grad1 = n;
                break;
            }
            case 2: {
                this.design.textColor = n;
                break;
            }
            case 1: {
                this.design.clockColor2 = n;
                this.design.twoTone = true;
                break;
            }
            case 0: {
                this.design.clockColor = n;
            }
        }
        boolean bl = this.design.autoColors;
        this.design.autoColors = false;
        this.commit();
        if (bl) {
            this.rebuild();
        }
    }

    private void applyWallpaper() {
        if (this.design.bgMode == 0 && !Store.hasBackground((Context)this)) {
            this.toast("\u0421\u043d\u0430\u0447\u0430\u043b\u0430 \u0432\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u0444\u043e\u0442\u043e \u0438\u043b\u0438 \u0432\u043a\u043b\u044e\u0447\u0438\u0442\u0435 \u0433\u0440\u0430\u0434\u0438\u0435\u043d\u0442");
            return;
        }
        Intent intent = new Intent("android.service.wallpaper.CHANGE_LIVE_WALLPAPER");
        intent.putExtra("android.service.wallpaper.extra.LIVE_WALLPAPER_COMPONENT", (Parcelable)new ComponentName((Context)this, ClockWallpaperService.class));
        try {
            this.startActivity(intent);
            this.toast("\u041d\u0430\u0436\u043c\u0438\u0442\u0435 \u00ab\u0423\u0441\u0442\u0430\u043d\u043e\u0432\u0438\u0442\u044c\u00bb \u0438 \u043e\u0442\u043c\u0435\u0442\u044c\u0442\u0435 \u044d\u043a\u0440\u0430\u043d \u0431\u043b\u043e\u043a\u0438\u0440\u043e\u0432\u043a\u0438");
        }
        catch (Exception exception) {
            try {
                Intent intent2 = new Intent("android.service.wallpaper.LIVE_WALLPAPER_CHOOSER");
                this.startActivity(intent2);
            }
            catch (Exception exception2) {
                this.toast("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043e\u0442\u043a\u0440\u044b\u0442\u044c \u0432\u044b\u0431\u043e\u0440 \u043e\u0431\u043e\u0435\u0432");
            }
        }
    }

    private View axis(String string2, int n, int n2, int n3, int n4, IntConsumer intConsumer) {
        Ui ui = this.ui;
        if (n4 != -9999) {
            n3 = n4;
        }
        return ui.slider(string2, n, n2, n3, "", new MainActivity$$ExternalSyntheticLambda80(this, intConsumer));
    }

    private void buildBackgroundTab() {
        Object object = this.ui.card(this.content, "\u0424\u043e\u0442\u043e");
        this.photoStatus = this.ui.label("", 14.0f, -6512986, false);
        object.addView((View)this.photoStatus);
        LinearLayout linearLayout = this.ui.hrow();
        linearLayout.addView((View)this.ui.pill("\u0412\u044b\u0431\u0440\u0430\u0442\u044c \u0444\u043e\u0442\u043e", true, new MainActivity$$ExternalSyntheticLambda54(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        linearLayout.addView(this.ui.space(8));
        linearLayout.addView((View)this.ui.pill("\u0412\u044b\u0440\u0435\u0437 (PNG)", false, new MainActivity$$ExternalSyntheticLambda61(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        object.addView((View)linearLayout);
        object.addView((View)this.ui.label("\u0412\u044b\u0440\u0435\u0437 \u2014 \u044d\u0442\u043e \u043e\u0431\u044a\u0435\u043a\u0442 \u0441 \u0444\u043e\u0442\u043e \u043d\u0430 \u043f\u0440\u043e\u0437\u0440\u0430\u0447\u043d\u043e\u043c \u0444\u043e\u043d\u0435. \u041d\u0430 Samsung: \u043e\u0442\u043a\u0440\u043e\u0439\u0442\u0435 \u0444\u043e\u0442\u043e \u0432 \u0413\u0430\u043b\u0435\u0440\u0435\u0435, \u0434\u043e\u043b\u0433\u043e \u043d\u0430\u0436\u043c\u0438\u0442\u0435 \u043d\u0430 \u043e\u0431\u044a\u0435\u043a\u0442 \u0438 \u0432\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u00ab\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c \u043a\u0430\u043a \u0438\u0437\u043e\u0431\u0440\u0430\u0436\u0435\u043d\u0438\u0435\u00bb. \u041f\u0440\u0438\u043b\u043e\u0436\u0435\u043d\u0438\u0435 \u0441\u0430\u043c\u043e \u043d\u0430\u0439\u0434\u0451\u0442 \u043c\u0435\u0441\u0442\u043e \u0432\u044b\u0440\u0435\u0437\u0430 \u043d\u0430 \u0444\u043e\u0442\u043e.", 12.0f, -6512986, false));
        linearLayout = this.ui.hrow();
        linearLayout.addView((View)this.ui.pill("\u2190", false, new MainActivity$$ExternalSyntheticLambda63(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        linearLayout.addView(this.ui.space(6));
        linearLayout.addView((View)this.ui.pill("\u2192", false, new MainActivity$$ExternalSyntheticLambda64(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        linearLayout.addView(this.ui.space(6));
        linearLayout.addView((View)this.ui.pill("\u2191", false, new MainActivity$$ExternalSyntheticLambda65(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        linearLayout.addView(this.ui.space(6));
        linearLayout.addView((View)this.ui.pill("\u2193", false, new MainActivity$$ExternalSyntheticLambda66(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        linearLayout.addView(this.ui.space(10));
        linearLayout.addView((View)this.ui.pill("\u0423\u0431\u0440\u0430\u0442\u044c \u0432\u044b\u0440\u0435\u0437", false, new MainActivity$$ExternalSyntheticLambda67(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        object.addView((View)linearLayout);
        linearLayout = this.ui.pill("\u0412\u044b\u0440\u0435\u0437\u0430\u0442\u044c \u043e\u0431\u044a\u0435\u043a\u0442 \u0430\u0432\u0442\u043e\u043c\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u0438 (\u0431\u0435\u0442\u0430)", false, new MainActivity$$ExternalSyntheticLambda68(this));
        Object object2 = this.ui.wrap();
        ((LinearLayout.LayoutParams)object2).topMargin = this.ui.dp(10);
        object.addView((View)linearLayout, (ViewGroup.LayoutParams)object2);
        object.addView((View)this.ui.label("\u0420\u0430\u0431\u043e\u0442\u0430\u0435\u0442 \u043d\u0430 \u0442\u0435\u043b\u0435\u0444\u043e\u043d\u0435 \u0447\u0435\u0440\u0435\u0437 Google. \u0415\u0441\u043b\u0438 \u043d\u0435 \u043f\u043e\u043b\u0443\u0447\u0438\u0442\u0441\u044f, \u043f\u043e\u043a\u0430\u0436\u0443 \u043f\u0440\u0438\u0447\u0438\u043d\u0443.", 12.0f, -6512986, false));
        this.refreshPhotoStatus();
        linearLayout = this.ui.card(this.content, "\u0422\u0438\u043f \u0444\u043e\u043d\u0430");
        object2 = this.ui;
        int n = this.design.bgMode;
        object = new MainActivity$$ExternalSyntheticLambda69(this);
        linearLayout.addView(((Ui)object2).chips(new String[]{"\u0424\u043e\u0442\u043e", "\u0413\u0440\u0430\u0434\u0438\u0435\u043d\u0442"}, n, (IntConsumer)object));
        if (this.design.bgMode == 1) {
            linearLayout.addView((View)this.ui.label("\u041f\u0435\u0440\u0432\u044b\u0439 \u0446\u0432\u0435\u0442", 13.0f, -6512986, false));
            linearLayout.addView(this.ui.swatches(GRADIENT_COLORS, this.design.grad1, new MainActivity$$ExternalSyntheticLambda70(this)));
            linearLayout.addView((View)this.ui.label("\u0412\u0442\u043e\u0440\u043e\u0439 \u0446\u0432\u0435\u0442", 13.0f, -6512986, false));
            linearLayout.addView(this.ui.swatches(GRADIENT_COLORS, this.design.grad2, new MainActivity$$ExternalSyntheticLambda55(this)));
            linearLayout.addView(this.ui.sw("\u041f\u043b\u0430\u0432\u043d\u043e\u0435 \u0434\u0432\u0438\u0436\u0435\u043d\u0438\u0435 \u0446\u0432\u0435\u0442\u0430", this.design.gradAnim, new MainActivity$$ExternalSyntheticLambda56(this)));
        }
        linearLayout.addView(this.ui.slider("\u0417\u0430\u0442\u0435\u043c\u043d\u0435\u043d\u0438\u0435", 0, 70, this.design.dim, "%", new MainActivity$$ExternalSyntheticLambda57(this)));
        object = this.ui.card(this.content, "\u0426\u0432\u0435\u0442 \u043e\u0431\u043e\u0435\u0432");
        object.addView(this.ui.slider("\u041e\u0442\u0442\u0435\u043d\u043e\u043a", -180, 180, this.design.hue, "\u00b0", new MainActivity$$ExternalSyntheticLambda58(this)));
        object.addView(this.ui.slider("\u041d\u0430\u0441\u044b\u0449\u0435\u043d\u043d\u043e\u0441\u0442\u044c", 0, 200, this.design.sat, "%", new MainActivity$$ExternalSyntheticLambda59(this)));
        object.addView((View)this.ui.pill("\u0421\u0431\u0440\u043e\u0441\u0438\u0442\u044c \u0446\u0432\u0435\u0442 \u043e\u0431\u043e\u0435\u0432", false, new MainActivity$$ExternalSyntheticLambda60(this)));
    }

    private void buildClockTab() {
        LinearLayout linearLayout;
        this.ui.card(this.content, null).addView(this.ui.sw("\u041f\u043e\u043a\u0430\u0437\u044b\u0432\u0430\u0442\u044c \u0447\u0430\u0441\u044b", this.design.clockShow, new MainActivity$$ExternalSyntheticLambda96(this)));
        this.content.addView((View)this.ui.label("\u0421\u0442\u0438\u043b\u0438", 16.0f, -723722, true), (ViewGroup.LayoutParams)this.titleLp());
        for (int i = 0; i < Design.STYLE_NAMES.length; i += 2) {
            linearLayout = this.ui.hrow();
            for (int j = i; j < Math.min(i + 2, Design.STYLE_NAMES.length); ++j) {
                linearLayout.addView(this.styleCard(j), (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
            }
            this.content.addView((View)linearLayout);
        }
        linearLayout = this.ui.card(this.content, "\u0426\u0438\u0444\u0440\u044b");
        linearLayout.addView(this.ui.slider("\u0420\u0430\u0437\u043c\u0435\u0440", 40, 200, this.design.clockSize, "", new MainActivity$$ExternalSyntheticLambda102(this)));
        linearLayout.addView(this.ui.slider("\u0420\u0430\u0441\u0441\u0442\u043e\u044f\u043d\u0438\u0435 \u043c\u0435\u0436\u0434\u0443 \u0446\u0438\u0444\u0440\u0430\u043c\u0438", -10, 40, this.design.letterSpacing, "", new MainActivity$$ExternalSyntheticLambda103(this)));
        linearLayout.addView(this.ui.slider("\u0420\u0430\u0441\u0441\u0442\u043e\u044f\u043d\u0438\u0435 \u043c\u0435\u0436\u0434\u0443 \u0440\u044f\u0434\u0430\u043c\u0438 (\u0432 \u0434\u0432\u0430 \u0440\u044f\u0434\u0430)", 10, 80, this.design.lineGap, "", new MainActivity$$ExternalSyntheticLambda104(this)));
        linearLayout.addView((View)this.ui.label("\u0420\u0430\u0437\u0434\u0435\u043b\u0438\u0442\u0435\u043b\u044c (\u0432 \u043e\u0434\u043d\u0443 \u0441\u0442\u0440\u043e\u043a\u0443)", 13.0f, -6512986, false));
        linearLayout.addView(this.ui.chips(Design.SEPARATOR_NAMES, this.design.separator, new MainActivity$$ExternalSyntheticLambda105(this)));
        linearLayout.addView(this.ui.sw("24-\u0447\u0430\u0441\u043e\u0432\u043e\u0439 \u0444\u043e\u0440\u043c\u0430\u0442", this.design.use24, new MainActivity$$ExternalSyntheticLambda1(this)));
        linearLayout.addView(this.ui.sw("\u0427\u0430\u0441\u044b \u043f\u043e\u0432\u0435\u0440\u0445 \u0432\u044b\u0440\u0435\u0437\u0430 (\u0430 \u043d\u0435 \u0437\u0430 \u043d\u0438\u043c)", this.design.clockOver, new MainActivity$$ExternalSyntheticLambda2(this)));
        linearLayout = this.ui.card(this.content, "\u0426\u0432\u0435\u0442 \u0438 \u0432\u0438\u0434");
        linearLayout.addView((View)this.ui.label("\u0426\u0432\u0435\u0442", 13.0f, -6512986, false));
        linearLayout.addView(this.ui.swatches(PALETTE, this.design.clockColor, new MainActivity$$ExternalSyntheticLambda3(this)));
        linearLayout.addView((View)this.ui.label("\u0412\u0442\u043e\u0440\u043e\u0439 \u0446\u0432\u0435\u0442 (\u043c\u0438\u043d\u0443\u0442\u044b, \u0433\u0440\u0430\u0434\u0438\u0435\u043d\u0442)", 13.0f, -6512986, false));
        linearLayout.addView(this.ui.swatches(PALETTE, this.design.clockColor2, new MainActivity$$ExternalSyntheticLambda4(this)));
        linearLayout.addView(this.ui.sw("\u0413\u0440\u0430\u0434\u0438\u0435\u043d\u0442 \u043d\u0430 \u0446\u0438\u0444\u0440\u0430\u0445", this.design.gradText, new MainActivity$$ExternalSyntheticLambda5(this)));
        linearLayout.addView(this.ui.slider("\u0423\u0433\u043e\u043b \u0433\u0440\u0430\u0434\u0438\u0435\u043d\u0442\u0430", 0, 360, this.design.gradAngle, "\u00b0", new MainActivity$$ExternalSyntheticLambda97(this)));
        linearLayout.addView(this.ui.sw("\u0422\u0435\u043d\u044c", this.design.shadow, new MainActivity$$ExternalSyntheticLambda98(this)));
        linearLayout.addView(this.ui.sw("\u041e\u0431\u0432\u043e\u0434\u043a\u0430", this.design.stroke, new MainActivity$$ExternalSyntheticLambda99(this)));
        linearLayout.addView(this.ui.slider("\u0422\u043e\u043b\u0449\u0438\u043d\u0430 \u043e\u0431\u0432\u043e\u0434\u043a\u0438", 1, 30, this.design.strokeW, "", new MainActivity$$ExternalSyntheticLambda100(this)));
        linearLayout.addView((View)this.ui.label("\u0426\u0432\u0435\u0442 \u043e\u0431\u0432\u043e\u0434\u043a\u0438", 13.0f, -6512986, false));
        linearLayout.addView(this.ui.swatches(PALETTE, this.design.strokeColor, new MainActivity$$ExternalSyntheticLambda101(this)));
        linearLayout = this.ui.card(this.content, "\u0428\u0440\u0438\u0444\u0442 \u0438 \u0444\u043e\u0440\u043c\u0430 \u0446\u0438\u0444\u0440");
        this.fontSection(linearLayout, this.design.font);
        linearLayout.addView((View)this.ui.label("\u041f\u043e\u043b\u043e\u0436\u0435\u043d\u0438\u0435, \u043d\u0430\u043a\u043b\u043e\u043d \u0438 \u043f\u043e\u0432\u043e\u0440\u043e\u0442 \u2014 \u043d\u0430 \u0432\u043a\u043b\u0430\u0434\u043a\u0435 \u00ab\u042d\u043b\u0435\u043c\u0435\u043d\u0442\u044b\u00bb \u0438\u043b\u0438 \u0449\u0438\u043f\u043a\u043e\u043c \u043d\u0430 \u043f\u0440\u0435\u0434\u043f\u0440\u043e\u0441\u043c\u043e\u0442\u0440\u0435.", 12.0f, -6512986, false));
        this.refreshMinis();
    }

    private void buildColorsTab() {
        Object object = this.ui.card(this.content, "\u0426\u0432\u0435\u0442\u0430 \u0441\u0438\u0441\u0442\u0435\u043c\u044b");
        if (!SystemColors.available()) {
            object.addView((View)this.ui.label("\u041f\u0430\u043b\u0438\u0442\u0440\u0430 \u0441\u0438\u0441\u0442\u0435\u043c\u044b \u0435\u0441\u0442\u044c \u043d\u0430 Android 12 \u0438 \u043d\u043e\u0432\u0435\u0435. \u041d\u0430 \u044d\u0442\u043e\u043c \u0442\u0435\u043b\u0435\u0444\u043e\u043d\u0435 \u0435\u0451 \u043d\u0435\u0442.", 14.0f, -6512986, false));
            return;
        }
        object.addView(this.ui.sw("\u041f\u043e\u0434\u0431\u0438\u0440\u0430\u0442\u044c \u0446\u0432\u0435\u0442\u0430 \u0430\u0432\u0442\u043e\u043c\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u0438", this.design.autoColors, new MainActivity$$ExternalSyntheticLambda90(this)));
        object.addView((View)this.ui.label("\u0427\u0430\u0441\u044b, \u043d\u0430\u0434\u043f\u0438\u0441\u0438 \u0438 \u0433\u0440\u0430\u0434\u0438\u0435\u043d\u0442 \u0432\u043e\u0437\u044c\u043c\u0443\u0442 \u0446\u0432\u0435\u0442\u0430 \u0438\u0437 \u043f\u0430\u043b\u0438\u0442\u0440\u044b \u0432\u0430\u0448\u0435\u0433\u043e \u0442\u0435\u043b\u0435\u0444\u043e\u043d\u0430 (\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438 \u2192 \u041e\u0431\u043e\u0438 \u0438 \u0441\u0442\u0438\u043b\u044c \u2192 \u0426\u0432\u0435\u0442\u043e\u0432\u0430\u044f \u043f\u0430\u043b\u0438\u0442\u0440\u0430) \u0438 \u043f\u043e\u043c\u0435\u043d\u044f\u044e\u0442\u0441\u044f \u0432\u043c\u0435\u0441\u0442\u0435 \u0441 \u043d\u0435\u0439.", 12.0f, -6512986, false));
        LinearLayout linearLayout = this.ui.card(this.content, "\u0412\u044b\u0431\u0440\u0430\u0442\u044c \u0432\u0440\u0443\u0447\u043d\u0443\u044e");
        linearLayout.addView((View)this.ui.label("\u041a\u0443\u0434\u0430 \u043f\u0440\u0438\u043c\u0435\u043d\u0438\u0442\u044c \u0446\u0432\u0435\u0442:", 13.0f, -6512986, false));
        linearLayout.addView(this.ui.chips(COLOR_TARGETS, this.colorTarget, new MainActivity$$ExternalSyntheticLambda91(this)));
        for (int i = 0; i < SystemColors.GROUPS.length; ++i) {
            linearLayout.addView((View)this.ui.label(SystemColors.GROUPS[i], 13.0f, -6512986, false));
            object = new int[SystemColors.TONES.length];
            for (int j = 0; j < ((LinearLayout)object).length; ++j) {
                object[j] = (LinearLayout)SystemColors.get((Context)this, i, SystemColors.TONES[j]);
            }
            linearLayout.addView(this.ui.swatches((int[])object, 1, new MainActivity$$ExternalSyntheticLambda92(this)));
        }
        linearLayout.addView((View)this.ui.label("\u0412\u044b\u0431\u0440\u0430\u043d\u043d\u044b\u0439 \u0432\u0440\u0443\u0447\u043d\u0443\u044e \u0446\u0432\u0435\u0442 \u043e\u0442\u043a\u043b\u044e\u0447\u0430\u0435\u0442 \u0430\u0432\u0442\u043e\u043c\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u0438\u0439 \u043f\u043e\u0434\u0431\u043e\u0440.", 12.0f, -6512986, false));
    }

    private void buildEffectsTab() {
        LinearLayout linearLayout = this.ui.card(this.content, "\u0421\u0434\u0432\u0438\u0433 \u043f\u0440\u0438 \u043d\u0430\u043a\u043b\u043e\u043d\u0435");
        linearLayout.addView(this.ui.sw("\u041e\u0431\u043e\u0438 \u0438 \u0432\u044b\u0440\u0435\u0437 \u0434\u0432\u0438\u0433\u0430\u044e\u0442\u0441\u044f \u043f\u0440\u0438 \u043d\u0430\u043a\u043b\u043e\u043d\u0435 \u0442\u0435\u043b\u0435\u0444\u043e\u043d\u0430", this.design.parallax, new MainActivity$$ExternalSyntheticLambda35(this)));
        linearLayout.addView(this.ui.slider("\u0421\u0438\u043b\u0430", 0, 100, this.design.parallaxStrength, "%", new MainActivity$$ExternalSyntheticLambda39(this)));
        linearLayout.addView(this.ui.sw("\u0427\u0430\u0441\u044b \u0438 \u043d\u0430\u0434\u043f\u0438\u0441\u0438 \u0442\u043e\u0436\u0435 \u0434\u0432\u0438\u0433\u0430\u044e\u0442\u0441\u044f", this.design.parallaxText, new MainActivity$$ExternalSyntheticLambda41(this)));
        linearLayout.addView((View)this.ui.label("\u041b\u0443\u0447\u0448\u0435 \u0432\u0441\u0435\u0433\u043e \u0441 \u0432\u044b\u0440\u0435\u0437\u043e\u043c: \u0447\u0430\u0441\u044b \u043e\u043a\u0430\u0436\u0443\u0442\u0441\u044f \u043c\u0435\u0436\u0434\u0443 \u0444\u043e\u043d\u043e\u043c \u0438 \u043e\u0431\u044a\u0435\u043a\u0442\u043e\u043c.", 12.0f, -6512986, false));
        linearLayout = this.ui.card(this.content, "\u041f\u043e\u0433\u043e\u0434\u0430 \u0437\u0430 \u043e\u043a\u043d\u043e\u043c");
        linearLayout.addView(this.ui.sw("\u042d\u0444\u0444\u0435\u043a\u0442\u044b \u043f\u043e \u0440\u0435\u0430\u043b\u044c\u043d\u043e\u0439 \u043f\u043e\u0433\u043e\u0434\u0435", this.design.weatherAuto, new MainActivity$$ExternalSyntheticLambda42(this)));
        linearLayout.addView((View)this.ui.label("\u0418\u0434\u0451\u0442 \u0434\u043e\u0436\u0434\u044c \u2014 \u043d\u0430 \u044d\u043a\u0440\u0430\u043d\u0435 \u0434\u043e\u0436\u0434\u044c, \u0441\u043d\u0435\u0433 \u2014 \u0441\u043d\u0435\u0433, \u0433\u0440\u043e\u0437\u0430 \u2014 \u0434\u043e\u0436\u0434\u044c \u0441\u043e \u0432\u0441\u043f\u044b\u0448\u043a\u0430\u043c\u0438. \u042f\u0441\u043d\u043e \u0438\u043b\u0438 \u043f\u0430\u0441\u043c\u0443\u0440\u043d\u043e \u2014 \u044d\u043a\u0440\u0430\u043d \u0447\u0438\u0441\u0442\u044b\u0439. \u041e\u0431\u043d\u043e\u0432\u043b\u044f\u0435\u0442\u0441\u044f \u0440\u0430\u0437 \u0432 30 \u043c\u0438\u043d\u0443\u0442.", 12.0f, -6512986, false));
        Object object = this.ui.edit("\u0412\u0430\u0448 \u0433\u043e\u0440\u043e\u0434, \u043d\u0430\u043f\u0440\u0438\u043c\u0435\u0440 \u041c\u043e\u0441\u043a\u0432\u0430", false);
        object.setInputType(1);
        object.setText((CharSequence)this.design.weatherCity);
        linearLayout.addView((View)object);
        linearLayout.addView((View)this.ui.pill("\u041d\u0430\u0439\u0442\u0438 \u0433\u043e\u0440\u043e\u0434 \u0438 \u043f\u043e\u0433\u043e\u0434\u0443", true, new MainActivity$$ExternalSyntheticLambda43(this, (EditText)object)));
        this.weatherResult = this.ui.label(this.weatherSummary(), 13.0f, -6512986, false);
        linearLayout.addView((View)this.weatherResult);
        linearLayout.addView((View)this.ui.label("\u041f\u0440\u043e\u0431\u043d\u044b\u0439 \u043f\u043e\u043a\u0430\u0437 (\u0442\u043e\u043b\u044c\u043a\u043e \u0437\u0434\u0435\u0441\u044c, \u043d\u0430 \u044d\u043a\u0440\u0430\u043d \u0431\u043b\u043e\u043a\u0438\u0440\u043e\u0432\u043a\u0438 \u043d\u0435 \u0432\u043b\u0438\u044f\u0435\u0442):", 12.0f, -6512986, false));
        Ui ui = this.ui;
        object = new MainActivity$$ExternalSyntheticLambda44(this, new int[]{-1, 0, 63, 73, 95});
        linearLayout.addView(ui.chips(new String[]{"\u0420\u0435\u0430\u043b\u044c\u043d\u0430\u044f", "\u042f\u0441\u043d\u043e", "\u0414\u043e\u0436\u0434\u044c", "\u0421\u043d\u0435\u0433", "\u0413\u0440\u043e\u0437\u0430"}, 0, (IntConsumer)object));
        linearLayout.addView((View)this.ui.label("\u0414\u0430\u043d\u043d\u044b\u0435 \u043e \u043f\u043e\u0433\u043e\u0434\u0435: Open-Meteo (\u0431\u0435\u0437 \u0440\u0435\u0433\u0438\u0441\u0442\u0440\u0430\u0446\u0438\u0438). \u041d\u0443\u0436\u0435\u043d \u0438\u043d\u0442\u0435\u0440\u043d\u0435\u0442.", 12.0f, -6512986, false));
        linearLayout = this.ui.card(this.content, "\u0427\u0430\u0441\u0442\u0438\u0446\u044b \u0432\u0440\u0443\u0447\u043d\u0443\u044e");
        linearLayout.addView((View)this.ui.label("\u0420\u0430\u0431\u043e\u0442\u0430\u044e\u0442, \u043a\u043e\u0433\u0434\u0430 \u043f\u043e\u0433\u043e\u0434\u043d\u044b\u0435 \u044d\u0444\u0444\u0435\u043a\u0442\u044b \u0432\u044b\u043a\u043b\u044e\u0447\u0435\u043d\u044b.", 12.0f, -6512986, false));
        linearLayout.addView(this.ui.chips(PARTICLE_NAMES, this.design.particles, new MainActivity$$ExternalSyntheticLambda45(this)));
        linearLayout.addView(this.ui.slider("\u041a\u043e\u043b\u0438\u0447\u0435\u0441\u0442\u0432\u043e", 10, 200, this.design.particleCount, "", new MainActivity$$ExternalSyntheticLambda46(this)));
        linearLayout.addView(this.ui.slider("\u0421\u043a\u043e\u0440\u043e\u0441\u0442\u044c", 20, 300, this.design.pSpeed, "%", new MainActivity$$ExternalSyntheticLambda47(this)));
        linearLayout.addView(this.ui.slider("\u0420\u0430\u0437\u043c\u0435\u0440", 30, 300, this.design.pSize, "%", new MainActivity$$ExternalSyntheticLambda48(this)));
        linearLayout.addView(this.ui.slider("\u042f\u0440\u043a\u043e\u0441\u0442\u044c", 10, 100, this.design.pAlpha, "%", new MainActivity$$ExternalSyntheticLambda36(this)));
        linearLayout.addView((View)this.ui.label("\u0426\u0432\u0435\u0442 \u0447\u0430\u0441\u0442\u0438\u0446", 13.0f, -6512986, false));
        linearLayout.addView(this.ui.swatches(PALETTE, this.design.pColor, new MainActivity$$ExternalSyntheticLambda37(this)));
        linearLayout.addView((View)this.ui.pill("\u0426\u0432\u0435\u0442 \u043f\u043e \u0443\u043c\u043e\u043b\u0447\u0430\u043d\u0438\u044e", false, new MainActivity$$ExternalSyntheticLambda38(this)));
        linearLayout.addView((View)this.ui.label("\u0427\u0430\u0441\u0442\u0438\u0446\u044b \u0438 \u0434\u0432\u0438\u0436\u0435\u043d\u0438\u0435 \u0440\u0430\u0441\u0445\u043e\u0434\u0443\u044e\u0442 \u043d\u0435\u043c\u043d\u043e\u0433\u043e \u0431\u043e\u043b\u044c\u0448\u0435 \u0431\u0430\u0442\u0430\u0440\u0435\u0438.", 12.0f, -6512986, false));
    }

    private void buildElementsTab() {
        if (this.design.pos(this.selectedId) == null) {
            this.selectedId = "clock";
        }
        this.preview.selected = this.selectedId;
        String string2 = this.selectedId;
        LinearLayout linearLayout = this.ui.card(this.content, "\u0412\u044b\u0431\u0440\u0430\u043d\u043d\u044b\u0439 \u044d\u043b\u0435\u043c\u0435\u043d\u0442");
        linearLayout.addView((View)this.ui.label("\u041d\u0430\u0436\u043c\u0438\u0442\u0435 \u043d\u0430 \u044d\u043b\u0435\u043c\u0435\u043d\u0442 \u0432 \u043f\u0440\u0435\u0434\u043f\u0440\u043e\u0441\u043c\u043e\u0442\u0440\u0435 \u0438\u043b\u0438 \u0432\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u0437\u0434\u0435\u0441\u044c.", 12.0f, -6512986, false));
        Object object = this.design.elements();
        Object object2 = new String[object.size()];
        int n = 0;
        for (int i = 0; i < object.size(); ++i) {
            object2[i] = object.get(i)[1];
            if (!object.get(i)[0].equals(string2)) continue;
            n = i;
        }
        linearLayout.addView(this.ui.chips((String[])object2, n, new MainActivity$$ExternalSyntheticLambda10(this, (List)object)));
        object2 = this.design.xf(string2);
        object = this.design.sizeRange(string2);
        linearLayout.addView(this.ui.slider("\u0420\u0430\u0437\u043c\u0435\u0440", (int)object[0], (int)object[1], this.design.size(string2), "", new MainActivity$$ExternalSyntheticLambda20(this, string2)));
        linearLayout.addView(this.ui.slider("\u041f\u043e\u0432\u043e\u0440\u043e\u0442", -180, 180, object2.rot, "\u00b0", new MainActivity$$ExternalSyntheticLambda21(this, (Design.Xf)object2)));
        linearLayout.addView(this.ui.slider("\u041d\u0430\u043a\u043b\u043e\u043d \u0432\u043f\u0435\u0440\u0451\u0434-\u043d\u0430\u0437\u0430\u0434", -80, 80, object2.rx, "\u00b0", new MainActivity$$ExternalSyntheticLambda22(this, (Design.Xf)object2)));
        linearLayout.addView(this.ui.slider("\u041d\u0430\u043a\u043b\u043e\u043d \u0432\u043b\u0435\u0432\u043e-\u0432\u043f\u0440\u0430\u0432\u043e", -80, 80, object2.ry, "\u00b0", new MainActivity$$ExternalSyntheticLambda23(this, (Design.Xf)object2)));
        linearLayout.addView(this.ui.slider("\u041f\u0435\u0440\u0435\u043a\u043e\u0441", -60, 60, object2.skew, "", new MainActivity$$ExternalSyntheticLambda24(this, (Design.Xf)object2)));
        linearLayout.addView(this.ui.slider("\u0420\u0430\u0441\u0442\u044f\u043d\u0443\u0442\u044c \u043f\u043e \u0448\u0438\u0440\u0438\u043d\u0435", 30, 300, object2.sx, "%", new MainActivity$$ExternalSyntheticLambda25(this, (Design.Xf)object2)));
        linearLayout.addView(this.ui.slider("\u0420\u0430\u0441\u0442\u044f\u043d\u0443\u0442\u044c \u043f\u043e \u0432\u044b\u0441\u043e\u0442\u0435", 30, 300, object2.sy, "%", new MainActivity$$ExternalSyntheticLambda26(this, (Design.Xf)object2)));
        linearLayout.addView((View)this.ui.pill("\u0421\u0431\u0440\u043e\u0441\u0438\u0442\u044c \u043f\u043e\u0432\u043e\u0440\u043e\u0442 \u0438 \u043d\u0430\u043a\u043b\u043e\u043d", false, new MainActivity$$ExternalSyntheticLambda27(this, (Design.Xf)object2)));
        if (string2.equals("clock")) {
            string2 = this.ui.card(this.content, "\u0427\u0430\u0441\u044b");
            string2.addView(this.ui.sw("\u041f\u043e\u043a\u0430\u0437\u044b\u0432\u0430\u0442\u044c \u0447\u0430\u0441\u044b", this.design.clockShow, new MainActivity$$ExternalSyntheticLambda28(this)));
            string2.addView((View)this.ui.label("\u0428\u0440\u0438\u0444\u0442, \u0446\u0432\u0435\u0442 \u0438 \u0441\u0442\u0438\u043b\u044c \u2014 \u043d\u0430 \u0432\u043a\u043b\u0430\u0434\u043a\u0435 \u00ab\u0427\u0430\u0441\u044b\u00bb.", 12.0f, -6512986, false));
        } else if (string2.equals("date")) {
            string2 = this.ui.card(this.content, "\u0414\u0430\u0442\u0430");
            string2.addView(this.ui.sw("\u041f\u043e\u043a\u0430\u0437\u044b\u0432\u0430\u0442\u044c \u0434\u0430\u0442\u0443", this.design.dateShow, new MainActivity$$ExternalSyntheticLambda11(this)));
            string2.addView((View)this.ui.label("\u0424\u043e\u0440\u043c\u0430\u0442", 13.0f, -6512986, false));
            string2.addView(this.ui.chips(Design.DATE_NAMES, this.design.dateFmt, new MainActivity$$ExternalSyntheticLambda12(this)));
            this.fontSection((LinearLayout)string2, this.design.dateFont);
        } else if (string2.equals("bat")) {
            string2 = this.ui.card(this.content, "\u0411\u0430\u0442\u0430\u0440\u0435\u044f");
            string2.addView(this.ui.sw("\u041f\u043e\u043a\u0430\u0437\u044b\u0432\u0430\u0442\u044c \u0437\u0430\u0440\u044f\u0434", this.design.batShow, new MainActivity$$ExternalSyntheticLambda13(this)));
            string2.addView((View)this.ui.label("\u0428\u0440\u0438\u0444\u0442 \u0443 \u0431\u0430\u0442\u0430\u0440\u0435\u0438 \u0442\u0430\u043a\u043e\u0439 \u0436\u0435, \u043a\u0430\u043a \u0443 \u0434\u0430\u0442\u044b.", 12.0f, -6512986, false));
        } else if (this.design.layer(string2) != null) {
            this.layerCard(this.design.layer(string2), string2);
        } else if (this.design.sticker(string2) != null) {
            linearLayout = this.ui.card(this.content, "\u0421\u0442\u0438\u043a\u0435\u0440");
            object = this.ui.hrow();
            object.addView((View)this.ui.pill("\u041a\u043e\u043f\u0438\u0440\u043e\u0432\u0430\u0442\u044c", false, new MainActivity$$ExternalSyntheticLambda14(this, string2)), (ViewGroup.LayoutParams)this.ui.wrap());
            object.addView(this.ui.space(8));
            object.addView((View)this.ui.pill("\u0423\u0434\u0430\u043b\u0438\u0442\u044c", false, new MainActivity$$ExternalSyntheticLambda15(this, string2)), (ViewGroup.LayoutParams)this.ui.wrap());
            linearLayout.addView((View)object);
        }
        linearLayout = this.ui.card(this.content, "\u0414\u043e\u0431\u0430\u0432\u0438\u0442\u044c \u043d\u0430 \u044d\u043a\u0440\u0430\u043d");
        string2 = this.ui.hrow();
        string2.addView((View)this.ui.pill("+ \u0422\u0435\u043a\u0441\u0442", true, new MainActivity$$ExternalSyntheticLambda16(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        if (!this.design.hasWebLayer()) {
            string2.addView(this.ui.space(8));
            string2.addView((View)this.ui.pill("+ \u0422\u0435\u043a\u0441\u0442 \u0438\u0437 \u0438\u043d\u0442\u0435\u0440\u043d\u0435\u0442\u0430", false, new MainActivity$$ExternalSyntheticLambda17(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        }
        linearLayout.addView((View)string2);
        linearLayout.addView((View)this.ui.label("\u0421\u0442\u0438\u043a\u0435\u0440\u044b:", 13.0f, -6512986, false));
        linearLayout.addView(this.emojiChips());
        this.ui.card(this.content, "\u041e\u0431\u0449\u0438\u0439 \u0446\u0432\u0435\u0442 \u043d\u0430\u0434\u043f\u0438\u0441\u0435\u0439").addView(this.ui.swatches(PALETTE, this.design.textColor, new MainActivity$$ExternalSyntheticLambda19(this)));
    }

    private void buildMineTab() {
        LinearLayout linearLayout = this.ui.card(this.content, "\u041c\u043e\u0438 \u0441\u0442\u0438\u043b\u0438");
        linearLayout.addView((View)this.ui.label("\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u0435 \u0442\u0435\u043a\u0443\u0449\u0438\u0439 \u0432\u0438\u0434 \u044d\u043a\u0440\u0430\u043d\u0430 (\u0447\u0430\u0441\u044b, \u043d\u0430\u0434\u043f\u0438\u0441\u0438, \u0441\u0442\u0438\u043a\u0435\u0440\u044b, \u044d\u0444\u0444\u0435\u043a\u0442\u044b), \u0447\u0442\u043e\u0431\u044b \u043f\u043e\u0442\u043e\u043c \u0432\u0435\u0440\u043d\u0443\u0442\u044c\u0441\u044f \u043a \u043d\u0435\u043c\u0443 \u043e\u0434\u043d\u0438\u043c \u043d\u0430\u0436\u0430\u0442\u0438\u0435\u043c. \u0424\u043e\u0442\u043e \u0438 \u0432\u044b\u0440\u0435\u0437 \u0432 \u0441\u0442\u0438\u043b\u044c \u043d\u0435 \u0432\u0445\u043e\u0434\u044f\u0442.", 12.0f, -6512986, false));
        linearLayout.addView((View)this.ui.pill("\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c \u0442\u0435\u043a\u0443\u0449\u0438\u0439 \u0441\u0442\u0438\u043b\u044c", true, new MainActivity$$ExternalSyntheticLambda72(this)));
        JSONArray jSONArray = Store.presets((Context)this);
        for (int i = 0; i < jSONArray.length(); ++i) {
            JSONObject jSONObject = jSONArray.optJSONObject(i);
            if (jSONObject == null) continue;
            linearLayout = this.ui.hrow();
            linearLayout.setPadding(this.ui.dp(14), this.ui.dp(10), this.ui.dp(10), this.ui.dp(10));
            linearLayout.setBackground((Drawable)this.ui.rr(-14935008, 20));
            linearLayout.setGravity(16);
            linearLayout.addView((View)this.ui.label(jSONObject.optString("name", "\u0421\u0442\u0438\u043b\u044c"), 15.0f, -723722, false), (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
            linearLayout.addView((View)this.ui.pill("\u041f\u0440\u0438\u043c\u0435\u043d\u0438\u0442\u044c", true, new MainActivity$$ExternalSyntheticLambda74(this, jSONObject)), (ViewGroup.LayoutParams)this.ui.wrap());
            linearLayout.addView(this.ui.space(6));
            linearLayout.addView((View)this.ui.pill("\u2715", false, new MainActivity$$ExternalSyntheticLambda75(this, i)), (ViewGroup.LayoutParams)this.ui.wrap());
            jSONObject = this.ui.match();
            jSONObject.topMargin = this.ui.dp(8);
            this.content.addView((View)linearLayout, (ViewGroup.LayoutParams)jSONObject);
        }
        this.ui.card(this.content, "\u0421\u0431\u0440\u043e\u0441").addView((View)this.ui.pill("\u0412\u0435\u0440\u043d\u0443\u0442\u044c \u0441\u0442\u0430\u043d\u0434\u0430\u0440\u0442\u043d\u044b\u0435 \u043d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438", false, new MainActivity$$ExternalSyntheticLambda76(this)));
    }

    private View buildNav() {
        this.nav = new LinearLayout((Context)this);
        this.nav.setOrientation(0);
        this.nav.setBackgroundColor(-15658732);
        this.nav.setPadding(this.ui.dp(6), this.ui.dp(6), this.ui.dp(6), this.ui.dp(8));
        for (int i = 0; i < TAB_NAMES.length; ++i) {
            LinearLayout linearLayout = new LinearLayout((Context)this);
            linearLayout.setOrientation(1);
            linearLayout.setGravity(1);
            TextView textView = this.ui.label(TAB_ICONS[i], 20.0f, -723722, false);
            textView.setGravity(17);
            textView.setPadding(this.ui.dp(13), this.ui.dp(3), this.ui.dp(13), this.ui.dp(3));
            TextView textView2 = this.ui.label(TAB_NAMES[i], 11.0f, -6512986, false);
            textView2.setGravity(17);
            linearLayout.addView((View)textView);
            linearLayout.addView((View)textView2);
            linearLayout.setOnClickListener((View.OnClickListener)new MainActivity$$ExternalSyntheticLambda94(this, i));
            this.nav.addView((View)linearLayout, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        }
        return this.nav;
    }

    private void commit() {
        this.design.save((Context)this);
        this.preview.invalidate();
        this.refreshMinis();
    }

    private void deleteUserFont(Design.FontSpec object) {
        if (!((Design.FontSpec)object).id.startsWith("user:")) {
            this.toast("\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043e\u0434\u0438\u043d \u0438\u0437 \u0441\u0432\u043e\u0438\u0445 \u0448\u0440\u0438\u0444\u0442\u043e\u0432");
            return;
        }
        object = ((Design.FontSpec)object).id;
        Fonts.deleteUser((Context)this, (String)object);
        this.fixFontsAfterDelete((String)object);
        this.commit();
        this.rebuild();
    }

    /*
     * Unable to fully structure code
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    private String displayName(Uri var1_1) {
        var3_4 = this.getContentResolver().query(var1_1 /* !! */ , null, null, null, null);
        if (var3_4 == null) ** GOTO lbl20
        if (!var3_4.moveToFirst() || (var2_6 = var3_4.getColumnIndex("_display_name")) < 0) ** GOTO lbl20
        var1_1 /* !! */  = var3_4.getString(var2_6);
        if (var3_4 == null) return var1_1 /* !! */ ;
        var3_4.close();
        return var1_1 /* !! */ ;
        catch (Throwable var1_2) {
            if (var3_4 == null) throw var1_2;
            try {
                var3_4.close();
                throw var1_2;
            }
            catch (Throwable var3_5) {
                try {
                    var1_2.addSuppressed(var3_5);
                    throw var1_2;
lbl20:
                    // 2 sources

                    if (var3_4 == null) return "\u0421\u0432\u043e\u0439 \u0448\u0440\u0438\u0444\u0442";
                    var3_4.close();
                    return "\u0421\u0432\u043e\u0439 \u0448\u0440\u0438\u0444\u0442";
                }
                catch (Exception var1_3) {
                    // empty catch block
                }
            }
        }
        return "\u0421\u0432\u043e\u0439 \u0448\u0440\u0438\u0444\u0442";
    }

    private View emojiChips() {
        HorizontalScrollView horizontalScrollView = new HorizontalScrollView((Context)this);
        horizontalScrollView.setHorizontalScrollBarEnabled(false);
        LinearLayout linearLayout = this.ui.hrow();
        for (String string2 : EMOJI) {
            TextView textView = this.ui.label(string2, 24.0f, -723722, false);
            textView.setPadding(this.ui.dp(12), this.ui.dp(8), this.ui.dp(12), this.ui.dp(8));
            textView.setBackground((Drawable)this.ui.rr(-14013647, 20));
            textView.setOnClickListener((View.OnClickListener)new MainActivity$$ExternalSyntheticLambda79(this, string2));
            string2 = this.ui.wrap();
            ((LinearLayout.LayoutParams)string2).rightMargin = this.ui.dp(6);
            linearLayout.addView((View)textView, (ViewGroup.LayoutParams)string2);
        }
        horizontalScrollView.addView((View)linearLayout);
        return horizontalScrollView;
    }

    private void findWeather(String string2) {
        if (string2.trim().isEmpty()) {
            this.toast("\u0412\u0432\u0435\u0434\u0438\u0442\u0435 \u0433\u043e\u0440\u043e\u0434");
            return;
        }
        if (this.weatherResult != null) {
            this.weatherResult.setText((CharSequence)"\u0418\u0449\u0443\u2026");
        }
        new Thread(new MainActivity$$ExternalSyntheticLambda50(this, string2)).start();
    }

    private void fixFontsAfterDelete(String string2) {
        if (this.design.font.id.equals(string2)) {
            this.design.font.id = "sys:0";
        }
        if (this.design.dateFont.id.equals(string2)) {
            this.design.dateFont.id = "sys:0";
        }
        for (Design.Layer layer : this.design.layers) {
            if (!layer.font.id.equals(string2)) continue;
            layer.font.id = "sys:0";
        }
    }

    private View fontChips(Design.FontSpec fontSpec) {
        HorizontalScrollView horizontalScrollView = new HorizontalScrollView((Context)this);
        horizontalScrollView.setHorizontalScrollBarEnabled(false);
        LinearLayout linearLayout = this.ui.hrow();
        List<Fonts.Entry> list = Fonts.list();
        ArrayList<TextView> arrayList = new ArrayList<TextView>();
        int n = 0;
        for (int i = 0; i < list.size(); ++i) {
            Fonts.Entry entry = list.get(i);
            if (entry.id.equals(fontSpec.id)) {
                n = i;
            }
            TextView textView = this.ui.label(entry.name, 16.0f, -723722, false);
            textView.setTypeface(Fonts.get(entry.id));
            textView.setPadding(this.ui.dp(16), this.ui.dp(9), this.ui.dp(16), this.ui.dp(9));
            arrayList.add(textView);
            textView.setOnClickListener((View.OnClickListener)new MainActivity$$ExternalSyntheticLambda34(this, arrayList, i, fontSpec, entry));
            entry = this.ui.wrap();
            ((LinearLayout.LayoutParams)entry).rightMargin = this.ui.dp(6);
            ((LinearLayout.LayoutParams)entry).topMargin = this.ui.dp(4);
            ((LinearLayout.LayoutParams)entry).bottomMargin = this.ui.dp(4);
            linearLayout.addView((View)textView, (ViewGroup.LayoutParams)entry);
        }
        this.ui.styleChips(arrayList, n);
        horizontalScrollView.addView((View)linearLayout);
        return horizontalScrollView;
    }

    private void fontSection(LinearLayout linearLayout, Design.FontSpec fontSpec) {
        linearLayout.addView((View)this.ui.label("\u0428\u0440\u0438\u0444\u0442", 13.0f, -6512986, false));
        linearLayout.addView(this.fontChips(fontSpec));
        linearLayout.addView((View)this.ui.label("\u041a\u043e\u043d\u0441\u0442\u0440\u0443\u043a\u0442\u043e\u0440 \u0448\u0440\u0438\u0444\u0442\u0430", 14.0f, -723722, true));
        String string2 = fontSpec.id;
        String string3 = FLEX_FONTS.contains(string2) ? "\u042d\u0442\u043e \u0433\u0438\u0431\u043a\u0438\u0439 \u0448\u0440\u0438\u0444\u0442: \u0440\u0430\u0431\u043e\u0442\u0430\u044e\u0442 \u0432\u0441\u0435 \u043f\u043e\u043b\u0437\u0443\u043d\u043a\u0438." : (WEIGHT_FONTS.contains(string2) ? "\u0423 \u044d\u0442\u043e\u0433\u043e \u0448\u0440\u0438\u0444\u0442\u0430 \u043c\u0435\u043d\u044f\u0435\u0442\u0441\u044f \u0442\u043e\u043b\u044c\u043a\u043e \u0442\u043e\u043b\u0449\u0438\u043d\u0430. \u0414\u043b\u044f \u043f\u043e\u043b\u043d\u043e\u0439 \u0441\u0432\u043e\u0431\u043e\u0434\u044b \u0432\u044b\u0431\u0435\u0440\u0438\u0442\u0435 Roboto Flex." : (string2.startsWith("user:") ? "\u0420\u0430\u0431\u043e\u0442\u0430\u0435\u0442, \u0435\u0441\u043b\u0438 \u0432\u0430\u0448 \u0444\u0430\u0439\u043b \u2014 \u0433\u0438\u0431\u043a\u0438\u0439 \u0448\u0440\u0438\u0444\u0442 (variable)." : "\u0423 \u044d\u0442\u043e\u0433\u043e \u0448\u0440\u0438\u0444\u0442\u0430 \u0444\u043e\u0440\u043c\u0430 \u043d\u0435 \u043c\u0435\u043d\u044f\u0435\u0442\u0441\u044f. \u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 Roboto Flex \u2014 \u043e\u043d \u0433\u0438\u0431\u043a\u0438\u0439."));
        linearLayout.addView((View)this.ui.label(string3, 12.0f, -6512986, false));
        if (!FLEX_FONTS.contains(string2)) {
            linearLayout.addView((View)this.ui.pill("\u0412\u043a\u043b\u044e\u0447\u0438\u0442\u044c \u0433\u0438\u0431\u043a\u0438\u0439 \u0448\u0440\u0438\u0444\u0442 Roboto Flex", false, new MainActivity$$ExternalSyntheticLambda0(this, fontSpec)));
        }
        linearLayout.addView(this.axis("\u0422\u043e\u043b\u0449\u0438\u043d\u0430", 100, 1000, 400, fontSpec.wght, new MainActivity$$ExternalSyntheticLambda29(fontSpec)));
        linearLayout.addView(this.axis("\u0428\u0438\u0440\u0438\u043d\u0430 (\u0441\u043f\u043b\u044e\u0441\u043d\u0443\u0442\u044c \u2014 \u0440\u0430\u0441\u0442\u044f\u043d\u0443\u0442\u044c)", 25, 151, 100, fontSpec.wdth, new MainActivity$$ExternalSyntheticLambda40(fontSpec)));
        linearLayout.addView(this.axis("\u041d\u0430\u043a\u043b\u043e\u043d", -10, 0, 0, fontSpec.slnt, new MainActivity$$ExternalSyntheticLambda51(fontSpec)));
        linearLayout.addView(this.axis("\u0412\u044b\u0441\u043e\u0442\u0430 \u0446\u0438\u0444\u0440", 560, 788, 738, fontSpec.hgt, new MainActivity$$ExternalSyntheticLambda62(fontSpec)));
        linearLayout.addView(this.axis("\u0422\u043e\u043b\u0449\u0438\u043d\u0430 \u0432\u0435\u0440\u0442\u0438\u043a\u0430\u043b\u044c\u043d\u044b\u0445 \u0448\u0442\u0440\u0438\u0445\u043e\u0432", 27, 175, 96, fontSpec.stem, new MainActivity$$ExternalSyntheticLambda73(fontSpec)));
        linearLayout.addView(this.axis("\u0422\u043e\u043b\u0449\u0438\u043d\u0430 \u0433\u043e\u0440\u0438\u0437\u043e\u043d\u0442\u0430\u043b\u044c\u043d\u044b\u0445 \u0448\u0442\u0440\u0438\u0445\u043e\u0432", 25, 135, 79, fontSpec.hstem, new MainActivity$$ExternalSyntheticLambda84(fontSpec)));
        linearLayout.addView(this.axis("\u0428\u0438\u0440\u0438\u043d\u0430 \u0432\u043d\u0443\u0442\u0440\u0438 \u0446\u0438\u0444\u0440\u044b", 323, 603, 468, fontSpec.cnt, new MainActivity$$ExternalSyntheticLambda95(fontSpec)));
        string3 = this.ui.hrow();
        string3.addView((View)this.ui.pill("\u0421\u0431\u0440\u043e\u0441\u0438\u0442\u044c \u0444\u043e\u0440\u043c\u0443", false, new MainActivity$$ExternalSyntheticLambda106(this, fontSpec)), (ViewGroup.LayoutParams)this.ui.wrap());
        string3.addView(this.ui.space(8));
        string3.addView((View)this.ui.pill("\u0421\u0432\u043e\u0439 \u0448\u0440\u0438\u0444\u0442\u2026", false, new MainActivity$$ExternalSyntheticLambda8(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        string3.addView(this.ui.space(8));
        string3.addView((View)this.ui.pill("\u0423\u0434\u0430\u043b\u0438\u0442\u044c \u0441\u0432\u043e\u0439", false, new MainActivity$$ExternalSyntheticLambda18(this, fontSpec)), (ViewGroup.LayoutParams)this.ui.wrap());
        linearLayout.addView((View)string3);
        linearLayout.addView((View)this.ui.label("\u0421\u0432\u043e\u0439 \u0448\u0440\u0438\u0444\u0442: \u0444\u0430\u0439\u043b TTF, OTF \u0438\u043b\u0438 TTC. \u0414\u0440\u0443\u0433\u0438\u0435 \u0444\u043e\u0440\u043c\u0430\u0442\u044b (\u043d\u0430\u043f\u0440\u0438\u043c\u0435\u0440 WOFF) Android \u043d\u0435 \u0447\u0438\u0442\u0430\u0435\u0442.", 12.0f, -6512986, false));
    }

    private /* synthetic */ void lambda$axis$45(IntConsumer intConsumer, int n) {
        intConsumer.accept(n);
        this.commit();
    }

    private /* synthetic */ void lambda$buildBackgroundTab$10(int n) {
        this.design.grad1 = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildBackgroundTab$11(int n) {
        this.design.grad2 = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildBackgroundTab$12(Boolean bl) {
        this.design.gradAnim = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildBackgroundTab$13(int n) {
        this.design.dim = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildBackgroundTab$14(int n) {
        this.design.hue = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildBackgroundTab$15(int n) {
        this.design.sat = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildBackgroundTab$16() {
        this.design.hue = 0;
        this.design.sat = 100;
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$buildBackgroundTab$2() {
        this.pickImage(1);
    }

    private /* synthetic */ void lambda$buildBackgroundTab$3() {
        this.pickImage(2);
    }

    private /* synthetic */ void lambda$buildBackgroundTab$4() {
        this.nudgeCut(-6, 0);
    }

    private /* synthetic */ void lambda$buildBackgroundTab$5() {
        this.nudgeCut(6, 0);
    }

    private /* synthetic */ void lambda$buildBackgroundTab$6() {
        this.nudgeCut(0, -6);
    }

    private /* synthetic */ void lambda$buildBackgroundTab$7() {
        this.nudgeCut(0, 6);
    }

    private /* synthetic */ void lambda$buildBackgroundTab$8() {
        Store.removeCut((Context)this);
        this.refreshAfterImages();
    }

    private /* synthetic */ void lambda$buildBackgroundTab$9(int n) {
        this.design.bgMode = n;
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$buildClockTab$18(Boolean bl) {
        this.design.clockShow = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$19(int n) {
        this.design.clockSize = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$20(int n) {
        this.design.letterSpacing = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$21(int n) {
        this.design.lineGap = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$22(int n) {
        this.design.separator = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$23(Boolean bl) {
        this.design.use24 = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$24(Boolean bl) {
        this.design.clockOver = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$25(int n) {
        this.design.clockColor = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$26(int n) {
        this.design.clockColor2 = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$27(Boolean bl) {
        this.design.gradText = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$28(int n) {
        this.design.gradAngle = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$29(Boolean bl) {
        this.design.shadow = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$30(Boolean bl) {
        this.design.stroke = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$31(int n) {
        this.design.strokeW = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildClockTab$32(int n) {
        this.design.strokeColor = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildColorsTab$47(Boolean bl) {
        this.design.autoColors = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildColorsTab$48(int n) {
        this.colorTarget = n;
    }

    private /* synthetic */ void lambda$buildEffectsTab$74(Boolean bl) {
        this.design.parallax = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildEffectsTab$75(int n) {
        this.design.parallaxStrength = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildEffectsTab$76(Boolean bl) {
        this.design.parallaxText = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildEffectsTab$77(Boolean bl) {
        this.design.weatherAuto = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildEffectsTab$78(EditText editText) {
        this.findWeather(editText.getText().toString());
    }

    private /* synthetic */ void lambda$buildEffectsTab$79(int[] nArray, int n) {
        if (n == 0) {
            this.design.attachRuntime((Context)this);
        } else {
            this.design.weatherCode = nArray[n];
        }
        if (!this.design.weatherAuto) {
            this.toast("\u0412\u043a\u043b\u044e\u0447\u0438\u0442\u0435 \u00ab\u042d\u0444\u0444\u0435\u043a\u0442\u044b \u043f\u043e \u0440\u0435\u0430\u043b\u044c\u043d\u043e\u0439 \u043f\u043e\u0433\u043e\u0434\u0435\u00bb");
        }
        this.preview.invalidate();
    }

    private /* synthetic */ void lambda$buildEffectsTab$80(int n) {
        this.design.particles = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildEffectsTab$81(int n) {
        this.design.particleCount = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildEffectsTab$82(int n) {
        this.design.pSpeed = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildEffectsTab$83(int n) {
        this.design.pSize = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildEffectsTab$84(int n) {
        this.design.pAlpha = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildEffectsTab$85(int n) {
        this.design.pColor = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildEffectsTab$86() {
        this.design.pColor = 0;
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$buildElementsTab$49(List list, int n) {
        this.select(((String[])list.get(n))[0]);
        this.rebuild();
    }

    private /* synthetic */ void lambda$buildElementsTab$50(String string2, int n) {
        this.design.setSize(string2, n);
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$51(Design.Xf xf, int n) {
        xf.rot = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$52(Design.Xf xf, int n) {
        xf.rx = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$53(Design.Xf xf, int n) {
        xf.ry = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$54(Design.Xf xf, int n) {
        xf.skew = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$55(Design.Xf xf, int n) {
        xf.sx = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$56(Design.Xf xf, int n) {
        xf.sy = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$57(Design.Xf xf) {
        xf.reset();
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$buildElementsTab$58(Boolean bl) {
        this.design.clockShow = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$59(Boolean bl) {
        this.design.dateShow = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$60(int n) {
        this.design.dateFmt = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$61(Boolean bl) {
        this.design.batShow = bl;
        this.commit();
    }

    private /* synthetic */ void lambda$buildElementsTab$62(String object) {
        if ((object = this.design.sticker((String)object)) == null) {
            return;
        }
        Design.Sticker sticker = new Design.Sticker(((Design.Sticker)object).s, Math.min(0.96f, ((Design.Sticker)object).x + 0.06f), Math.min(0.97f, ((Design.Sticker)object).y + 0.04f));
        sticker.size = ((Design.Sticker)object).size;
        sticker.xf = ((Design.Sticker)object).xf.copy();
        this.design.stickers.add(sticker);
        this.select("st" + (this.design.stickers.size() - 1));
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$buildElementsTab$63(String string2) {
        this.design.stickers.remove(this.design.sticker(string2));
        this.select("clock");
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$buildElementsTab$64() {
        this.addLayer(false);
    }

    private /* synthetic */ void lambda$buildElementsTab$65() {
        this.addLayer(true);
    }

    private /* synthetic */ void lambda$buildElementsTab$66(int n) {
        this.design.textColor = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildMineTab$90(JSONObject jSONObject) {
        if ((jSONObject = jSONObject.optJSONObject("design")) != null) {
            this.setDesign(Design.fromJson(jSONObject));
        }
    }

    private /* synthetic */ void lambda$buildMineTab$91(int n) {
        Store.deletePreset((Context)this, n);
        this.showTab(5);
    }

    private /* synthetic */ void lambda$buildMineTab$92(DialogInterface dialogInterface, int n) {
        this.setDesign(new Design());
    }

    private /* synthetic */ void lambda$buildMineTab$93() {
        new AlertDialog.Builder((Context)this, 16974374).setMessage((CharSequence)"\u0412\u0435\u0440\u043d\u0443\u0442\u044c \u0432\u0441\u0435 \u043d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438 \u043a \u0441\u0442\u0430\u043d\u0434\u0430\u0440\u0442\u043d\u044b\u043c? \u0424\u043e\u0442\u043e \u0438 \u0432\u044b\u0440\u0435\u0437 \u043e\u0441\u0442\u0430\u043d\u0443\u0442\u0441\u044f.").setPositiveButton((CharSequence)"\u0412\u0435\u0440\u043d\u0443\u0442\u044c", (DialogInterface.OnClickListener)new MainActivity$$ExternalSyntheticLambda9(this)).setNegativeButton((CharSequence)"\u041e\u0442\u043c\u0435\u043d\u0430", null).show();
    }

    private /* synthetic */ void lambda$buildNav$0(int n, View view) {
        this.showTab(n);
    }

    private /* synthetic */ void lambda$emojiChips$71(String string2, View view) {
        if (this.design.stickers.size() >= 30) {
            this.toast("\u0421\u043b\u0438\u0448\u043a\u043e\u043c \u043c\u043d\u043e\u0433\u043e \u0441\u0442\u0438\u043a\u0435\u0440\u043e\u0432");
            return;
        }
        this.design.stickers.add(new Design.Sticker(string2, 0.5f, 0.85f));
        this.select("st" + (this.design.stickers.size() - 1));
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$findWeather$87(Store.Place place) {
        this.design.weatherCity = place.name;
        this.design.weatherLat = place.lat;
        this.design.weatherLon = place.lon;
        this.design.attachRuntime((Context)this);
        this.commit();
        if (this.weatherResult != null) {
            this.weatherResult.setText((CharSequence)this.weatherSummary());
        }
    }

    private /* synthetic */ void lambda$findWeather$88(String string2) {
        if (this.weatherResult != null) {
            this.weatherResult.setText((CharSequence)string2);
        }
    }

    private /* synthetic */ void lambda$findWeather$89(String object) {
        try {
            Store.Place place = Store.geocode((String)object);
            Store.saveWeather((Context)this, Store.fetchWeather(place.lat, place.lon));
            object = new MainActivity$$ExternalSyntheticLambda32(this, place);
            this.runOnUiThread((Runnable)object);
            return;
        }
        catch (Exception exception) {
            this.runOnUiThread(new MainActivity$$ExternalSyntheticLambda33(this, "\u041d\u0435 \u043f\u043e\u043b\u0443\u0447\u0438\u043b\u043e\u0441\u044c: " + exception.getMessage()));
            return;
        }
    }

    private /* synthetic */ void lambda$fontChips$46(List list, int n, Design.FontSpec fontSpec, Fonts.Entry entry, View view) {
        this.ui.styleChips(list, n);
        fontSpec.id = entry.id;
        this.commit();
    }

    private /* synthetic */ void lambda$fontSection$34(Design.FontSpec fontSpec) {
        fontSpec.id = "asset:RobotoFlex.ttf";
        this.commit();
        this.rebuild();
    }

    static /* synthetic */ void lambda$fontSection$35(Design.FontSpec fontSpec, int n) {
        fontSpec.wght = n;
    }

    static /* synthetic */ void lambda$fontSection$36(Design.FontSpec fontSpec, int n) {
        fontSpec.wdth = n;
    }

    static /* synthetic */ void lambda$fontSection$37(Design.FontSpec fontSpec, int n) {
        fontSpec.slnt = n;
    }

    static /* synthetic */ void lambda$fontSection$38(Design.FontSpec fontSpec, int n) {
        fontSpec.hgt = n;
    }

    static /* synthetic */ void lambda$fontSection$39(Design.FontSpec fontSpec, int n) {
        fontSpec.stem = n;
    }

    static /* synthetic */ void lambda$fontSection$40(Design.FontSpec fontSpec, int n) {
        fontSpec.hstem = n;
    }

    static /* synthetic */ void lambda$fontSection$41(Design.FontSpec fontSpec, int n) {
        fontSpec.cnt = n;
    }

    private /* synthetic */ void lambda$fontSection$42(Design.FontSpec fontSpec) {
        fontSpec.resetAxes();
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$fontSection$43() {
        this.pickFile(3);
    }

    private /* synthetic */ void lambda$fontSection$44(Design.FontSpec fontSpec) {
        this.deleteUserFont(fontSpec);
    }

    private /* synthetic */ void lambda$layerCard$67(Design.Layer layer, int n) {
        layer.color = n;
        this.commit();
    }

    private /* synthetic */ void lambda$layerCard$68(Design.Layer layer) {
        layer.color = 0;
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$layerCard$69(Design.Layer layer) {
        if (layer.web) {
            this.toast("\u041d\u0430\u0434\u043f\u0438\u0441\u044c \u0438\u0437 \u0438\u043d\u0442\u0435\u0440\u043d\u0435\u0442\u0430 \u043c\u043e\u0436\u0435\u0442 \u0431\u044b\u0442\u044c \u043e\u0434\u043d\u0430");
            return;
        }
        this.design.layers.add(layer.copy());
        this.select("tl" + (this.design.layers.size() - 1));
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$layerCard$70(Design.Layer layer) {
        this.design.layers.remove(layer);
        this.select("clock");
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$nudgeCut$17(int n, int n2) {
        try {
            Store.nudge((Context)this, n, n2);
        }
        catch (Exception exception) {
            // empty catch block
        }
        this.runOnUiThread(new MainActivity$$ExternalSyntheticLambda77(this));
    }

    private /* synthetic */ void lambda$onActivityResult$95(String string2) {
        Design.FontSpec fontSpec;
        Design.FontSpec fontSpec2 = fontSpec = this.design.fontOf(this.selectedId);
        if (fontSpec == null) {
            fontSpec2 = this.design.font;
        }
        fontSpec2.id = string2;
        this.commit();
        this.rebuild();
    }

    private /* synthetic */ void lambda$onActivityResult$96(String string2, String string3) {
        if (string2 != null) {
            this.showError("\u041d\u0435 \u043f\u043e\u043b\u0443\u0447\u0438\u043b\u043e\u0441\u044c", string2);
        } else {
            this.toast(string3);
        }
        this.refreshAfterImages();
    }

    /*
     * Enabled aggressive block sorting
     * Enabled unnecessary exception pruning
     * Enabled aggressive exception aggregation
     */
    private /* synthetic */ void lambda$onActivityResult$97(int n, Uri object) {
        String string2;
        block6: {
            Exception exception2;
            Object var4_6;
            block8: {
                Object var5_5;
                block7: {
                    var5_5 = null;
                    var4_6 = null;
                    string2 = null;
                    if (n == 3) {
                        try {
                            object = Fonts.importFont((Context)this, object, this.displayName((Uri)object));
                            MainActivity$$ExternalSyntheticLambda6 mainActivity$$ExternalSyntheticLambda6 = new MainActivity$$ExternalSyntheticLambda6(this, (String)object);
                            this.runOnUiThread(mainActivity$$ExternalSyntheticLambda6);
                            object = "\u0428\u0440\u0438\u0444\u0442 \u0434\u043e\u0431\u0430\u0432\u043b\u0435\u043d \u0438 \u0432\u044b\u0431\u0440\u0430\u043d";
                            break block6;
                        }
                        catch (OutOfMemoryError outOfMemoryError) {
                            break block7;
                        }
                        catch (Exception exception2) {
                            break block8;
                        }
                    }
                    if (n == 1) {
                        Store.importBackground((Context)this, object);
                        object = "\u0424\u043e\u0442\u043e \u0437\u0430\u0433\u0440\u0443\u0436\u0435\u043d\u043e";
                        break block6;
                    } else {
                        object = Store.importCut((Context)this, object);
                    }
                    break block6;
                }
                string2 = "\u041d\u0435 \u0445\u0432\u0430\u0442\u0438\u043b\u043e \u043f\u0430\u043c\u044f\u0442\u0438: \u043a\u0430\u0440\u0442\u0438\u043d\u043a\u0430 \u0441\u043b\u0438\u0448\u043a\u043e\u043c \u0431\u043e\u043b\u044c\u0448\u0430\u044f";
                object = var5_5;
                break block6;
            }
            string2 = String.valueOf(exception2.getMessage());
            object = var4_6;
        }
        this.runOnUiThread(new MainActivity$$ExternalSyntheticLambda7(this, string2, (String)object));
    }

    private /* synthetic */ void lambda$rebuild$1(int n) {
        this.scroll.scrollTo(0, n);
    }

    private /* synthetic */ void lambda$runAutoCut$98(String string2, String string3) {
        if (string2 != null) {
            this.showError("\u0410\u0432\u0442\u043e\u0432\u044b\u0440\u0435\u0437\u0430\u043d\u0438\u0435 \u043d\u0435 \u043f\u043e\u043b\u0443\u0447\u0438\u043b\u043e\u0441\u044c", string2);
        } else {
            this.toast(string3);
        }
        this.refreshAfterImages();
    }

    private /* synthetic */ void lambda$runAutoCut$99() {
        String string2 = null;
        String string3 = null;
        String string4 = null;
        try {
            String string5;
            string3 = string5 = Store.autoCut((Context)this);
            string2 = string4;
        }
        catch (Throwable throwable) {
            string4 = throwable.getClass().getSimpleName() + ": " + throwable.getMessage();
            string3 = string2;
            string2 = string4;
        }
        catch (Exception exception) {
            string2 = String.valueOf(exception.getMessage());
        }
        this.runOnUiThread(new MainActivity$$ExternalSyntheticLambda52(this, string2, string3));
    }

    private /* synthetic */ void lambda$savePresetDialog$94(EditText object, DialogInterface dialogInterface, int n) {
        block3: {
            object = object.getText().toString().trim();
            if (!((String)object).isEmpty()) break block3;
            object = "\u0421\u0442\u0438\u043b\u044c";
        }
        try {
            Store.addPreset((Context)this, (String)object, this.design);
            this.showTab(5);
            this.toast("\u0421\u0442\u0438\u043b\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0451\u043d");
        }
        catch (Exception exception) {
            this.toast("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c");
        }
    }

    private /* synthetic */ void lambda$styleCard$33(int n, View view) {
        Design.applyStyle(this.design, n);
        this.commit();
    }

    private /* synthetic */ void lambda$testFetch$72(String string2) {
        if (this.fetchResult != null) {
            this.fetchResult.setText((CharSequence)string2);
        }
        this.preview.invalidate();
    }

    private /* synthetic */ void lambda$testFetch$73(String string2) {
        try {
            string2 = Store.fetchText(string2);
            this.sp.edit().putString("fetchedText", string2).putLong("fetchedAt", System.currentTimeMillis()).apply();
            StringBuilder stringBuilder = new StringBuilder();
            string2 = stringBuilder.append("\u041f\u043e\u043b\u0443\u0447\u0438\u043b\u043e\u0441\u044c:\n").append(string2).toString();
        }
        catch (Exception exception) {
            string2 = "\u041d\u0435 \u043f\u043e\u043b\u0443\u0447\u0438\u043b\u043e\u0441\u044c: " + exception.getMessage();
        }
        this.runOnUiThread(new MainActivity$$ExternalSyntheticLambda81(this, string2));
    }

    private void layerCard(Design.Layer layer, String string2) {
        Ui ui = this.ui;
        LinearLayout linearLayout = this.content;
        string2 = layer.web ? "\u0422\u0435\u043a\u0441\u0442 \u0438\u0437 \u0438\u043d\u0442\u0435\u0440\u043d\u0435\u0442\u0430" : "\u0422\u0435\u043a\u0441\u0442";
        string2 = ui.card(linearLayout, string2);
        if (!layer.web) {
            linearLayout = this.ui.edit("\u041b\u044e\u0431\u043e\u0439 \u0442\u0435\u043a\u0441\u0442, \u043c\u043e\u0436\u043d\u043e \u0432 \u043d\u0435\u0441\u043a\u043e\u043b\u044c\u043a\u043e \u0441\u0442\u0440\u043e\u043a", true);
            linearLayout.setText((CharSequence)layer.text);
            linearLayout.addTextChangedListener((TextWatcher)new Ui.SimpleWatcher(this, layer){
                final MainActivity this$0;
                final Design.Layer val$l;
                {
                    this.this$0 = mainActivity;
                    this.val$l = layer;
                }

                public void afterTextChanged(Editable editable) {
                    this.val$l.text = editable.toString();
                    this.this$0.commit();
                }
            });
            string2.addView((View)linearLayout);
        } else {
            linearLayout = this.ui.edit("\u0421\u0441\u044b\u043b\u043a\u0430 https://\u2026 (\u0441\u0442\u0440\u0430\u043d\u0438\u0446\u0430 \u0438\u043b\u0438 \u043e\u0431\u044b\u0447\u043d\u044b\u0439 \u0442\u0435\u043a\u0441\u0442)", false);
            linearLayout.setText((CharSequence)layer.url);
            linearLayout.addTextChangedListener((TextWatcher)new Ui.SimpleWatcher(this, layer){
                final MainActivity this$0;
                final Design.Layer val$l;
                {
                    this.this$0 = mainActivity;
                    this.val$l = layer;
                }

                public void afterTextChanged(Editable editable) {
                    this.val$l.url = editable.toString().trim();
                    this.this$0.sp.edit().putString("fetchedText", "").putLong("fetchedAt", 0L).apply();
                    this.this$0.commit();
                }
            });
            string2.addView((View)linearLayout);
            linearLayout = this.ui.edit("\u041e\u0431\u043d\u043e\u0432\u043b\u044f\u0442\u044c \u0440\u0430\u0437 \u0432 \u2026 \u043c\u0438\u043d\u0443\u0442", false);
            linearLayout.setInputType(2);
            linearLayout.setText((CharSequence)String.valueOf(layer.refreshMin));
            linearLayout.addTextChangedListener((TextWatcher)new Ui.SimpleWatcher(this, layer){
                final MainActivity this$0;
                final Design.Layer val$l;
                {
                    this.this$0 = mainActivity;
                    this.val$l = layer;
                }

                public void afterTextChanged(Editable editable) {
                    try {
                        this.val$l.refreshMin = Math.max(1, Integer.parseInt(editable.toString().trim()));
                        this.this$0.commit();
                    }
                    catch (NumberFormatException numberFormatException) {
                        // empty catch block
                    }
                }
            });
            string2.addView((View)linearLayout);
            string2.addView((View)this.ui.pill("\u041f\u0440\u043e\u0432\u0435\u0440\u0438\u0442\u044c \u0441\u0441\u044b\u043b\u043a\u0443", false, new MainActivity$$ExternalSyntheticLambda85(this)));
            this.fetchResult = this.ui.label("", 13.0f, -6512986, false);
            string2.addView((View)this.fetchResult);
        }
        string2.addView((View)this.ui.label("\u0426\u0432\u0435\u0442", 13.0f, -6512986, false));
        string2.addView(this.ui.swatches(PALETTE, layer.color, new MainActivity$$ExternalSyntheticLambda86(this, layer)));
        string2.addView((View)this.ui.pill("\u041e\u0431\u0449\u0438\u0439 \u0446\u0432\u0435\u0442", false, new MainActivity$$ExternalSyntheticLambda87(this, layer)));
        this.fontSection((LinearLayout)string2, layer.font);
        linearLayout = this.ui.hrow();
        linearLayout.addView((View)this.ui.pill("\u041a\u043e\u043f\u0438\u0440\u043e\u0432\u0430\u0442\u044c", false, new MainActivity$$ExternalSyntheticLambda88(this, layer)), (ViewGroup.LayoutParams)this.ui.wrap());
        linearLayout.addView(this.ui.space(8));
        linearLayout.addView((View)this.ui.pill("\u0423\u0434\u0430\u043b\u0438\u0442\u044c", false, new MainActivity$$ExternalSyntheticLambda89(this, layer)), (ViewGroup.LayoutParams)this.ui.wrap());
        string2.addView((View)linearLayout);
    }

    private void nudgeCut(int n, int n2) {
        new Thread(new MainActivity$$ExternalSyntheticLambda83(this, n, n2)).start();
    }

    private void openEditor() {
        this.fromEditor = true;
        Intent intent = new Intent((Context)this, EditorActivity.class);
        intent.putExtra("selected", this.selectedId);
        this.startActivity(intent);
    }

    private void pickFile(int n) {
        Intent intent = new Intent("android.intent.action.GET_CONTENT");
        intent.setType("*/*");
        intent.addCategory("android.intent.category.OPENABLE");
        try {
            this.startActivityForResult(Intent.createChooser((Intent)intent, (CharSequence)"\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u0444\u0430\u0439\u043b \u0448\u0440\u0438\u0444\u0442\u0430"), n);
        }
        catch (Exception exception) {
            this.toast("\u041d\u0435 \u043d\u0430\u0448\u0451\u043b, \u0447\u0435\u043c \u0432\u044b\u0431\u0440\u0430\u0442\u044c \u0444\u0430\u0439\u043b");
        }
    }

    private void pickImage(int n) {
        Intent intent;
        if (Build.VERSION.SDK_INT >= 33) {
            intent = new Intent("android.provider.action.PICK_IMAGES");
        } else {
            intent = new Intent("android.intent.action.GET_CONTENT");
            intent.setType("image/*");
            intent.addCategory("android.intent.category.OPENABLE");
        }
        try {
            this.startActivityForResult(intent, n);
        }
        catch (Exception exception) {
            try {
                Intent intent2 = new Intent("android.intent.action.GET_CONTENT");
                intent2.setType("image/*");
                intent2.addCategory("android.intent.category.OPENABLE");
                this.startActivityForResult(Intent.createChooser((Intent)intent2, (CharSequence)"\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043a\u0430\u0440\u0442\u0438\u043d\u043a\u0443"), n);
            }
            catch (Exception exception2) {
                this.showError("\u041d\u0435 \u043d\u0430\u0448\u0451\u043b, \u0447\u0435\u043c \u0432\u044b\u0431\u0440\u0430\u0442\u044c \u043a\u0430\u0440\u0442\u0438\u043d\u043a\u0443", String.valueOf(exception2.getMessage()));
            }
        }
    }

    private void rebuild() {
        int n = this.scroll.getScrollY();
        this.showTab(this.tab);
        this.scroll.post((Runnable)new MainActivity$$ExternalSyntheticLambda71(this, n));
    }

    private void refreshAfterImages() {
        this.refreshPhotoStatus();
        this.preview.invalidate();
        this.refreshMinis();
    }

    private void refreshMinis() {
        for (PreviewView previewView : this.minis) {
            Object object = previewView.getTag();
            if (object instanceof Integer) {
                previewView.design = this.styled((Integer)object);
                previewView.selectedCard = Design.matchesStyle(this.design, (Integer)object);
            }
            previewView.invalidate();
        }
    }

    private void refreshPhotoStatus() {
        if (this.photoStatus == null) {
            return;
        }
        TextView textView = this.photoStatus;
        StringBuilder stringBuilder = new StringBuilder().append("\u0424\u043e\u0442\u043e: ");
        boolean bl = Store.hasBackground((Context)this);
        String string2 = "\u0435\u0441\u0442\u044c";
        String string3 = bl ? "\u0435\u0441\u0442\u044c" : "\u043d\u0435\u0442";
        stringBuilder = stringBuilder.append(string3).append("     \u0412\u044b\u0440\u0435\u0437: ");
        string3 = Store.hasCut((Context)this) ? string2 : "\u043d\u0435\u0442";
        textView.setText((CharSequence)stringBuilder.append(string3).toString());
    }

    private void runAutoCut() {
        if (!Store.hasBackground((Context)this)) {
            this.toast("\u0421\u043d\u0430\u0447\u0430\u043b\u0430 \u0432\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u0444\u043e\u0442\u043e");
            return;
        }
        this.toast("\u0412\u044b\u0440\u0435\u0437\u0430\u044e\u2026 \u044d\u0442\u043e \u043c\u043e\u0436\u0435\u0442 \u0437\u0430\u043d\u044f\u0442\u044c \u0434\u043e \u043c\u0438\u043d\u0443\u0442\u044b");
        new Thread(new MainActivity$$ExternalSyntheticLambda53(this)).start();
    }

    private void savePresetDialog() {
        EditText editText = this.ui.edit("\u041d\u0430\u0437\u0432\u0430\u043d\u0438\u0435 \u0441\u0442\u0438\u043b\u044f", false);
        editText.setText((CharSequence)("\u0421\u0442\u0438\u043b\u044c " + (Store.presets((Context)this).length() + 1)));
        new AlertDialog.Builder((Context)this, 16974374).setTitle((CharSequence)"\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c \u0441\u0442\u0438\u043b\u044c").setView((View)editText).setPositiveButton((CharSequence)"\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c", (DialogInterface.OnClickListener)new MainActivity$$ExternalSyntheticLambda78(this, editText)).setNegativeButton((CharSequence)"\u041e\u0442\u043c\u0435\u043d\u0430", null).show();
    }

    private void select(String string2) {
        this.selectedId = string2;
        this.preview.selected = string2;
        this.preview.invalidate();
    }

    private void setDesign(Design design) {
        design.attachRuntime((Context)this);
        this.design = design;
        this.preview.design = design;
        this.preview.selected = this.selectedId = "clock";
        this.commit();
        this.showTab(this.tab);
    }

    private void showError(String string2, String string3) {
        new AlertDialog.Builder((Context)this, 16974374).setTitle((CharSequence)string2).setMessage((CharSequence)string3).setPositiveButton((CharSequence)"\u041f\u043e\u043d\u044f\u0442\u043d\u043e", null).show();
    }

    private void showTab(int n) {
        this.tab = n;
        this.minis.clear();
        this.photoStatus = null;
        this.fetchResult = null;
        this.weatherResult = null;
        this.content.removeAllViews();
        this.updateNav();
        switch (n) {
            default: {
                this.buildMineTab();
                break;
            }
            case 4: {
                this.buildEffectsTab();
                break;
            }
            case 3: {
                this.buildElementsTab();
                break;
            }
            case 2: {
                this.buildColorsTab();
                break;
            }
            case 1: {
                this.buildClockTab();
                break;
            }
            case 0: {
                this.buildBackgroundTab();
            }
        }
        this.scroll.scrollTo(0, 0);
    }

    private View styleCard(int n) {
        LinearLayout linearLayout = new LinearLayout((Context)this);
        linearLayout.setOrientation(1);
        linearLayout.setPadding(this.ui.dp(5), this.ui.dp(5), this.ui.dp(5), this.ui.dp(8));
        PreviewView previewView = new PreviewView((Context)this);
        previewView.cropTop = true;
        previewView.design = this.styled(n);
        previewView.setTag(n);
        int n2 = (this.getResources().getDisplayMetrics().widthPixels - this.ui.dp(14) * 2 - this.ui.dp(5) * 4) / 2;
        linearLayout.addView((View)previewView, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(n2, Math.round((float)n2 * 1.05f)));
        TextView textView = this.ui.label(Design.STYLE_NAMES[n], 13.0f, -723722, false);
        textView.setGravity(17);
        linearLayout.addView((View)textView);
        previewView.setOnClickListener(new MainActivity$$ExternalSyntheticLambda82(this, n));
        this.minis.add(previewView);
        previewView.selectedCard = Design.matchesStyle(this.design, n);
        return linearLayout;
    }

    private Design styled(int n) {
        Design design = this.design.copy();
        Design.applyStyle(design, n);
        design.clockX = 0.5f;
        design.clockY = 0.3f;
        design.clockSize = Math.max(70, Math.min(120, design.clockSize));
        design.clockXf.reset();
        design.dateShow = false;
        design.batShow = false;
        design.layers.clear();
        design.stickers.clear();
        design.particles = 0;
        design.weatherAuto = false;
        return design;
    }

    private void testFetch() {
        Object object = this.design.webLayer();
        if (object == null) {
            if (this.fetchResult != null) {
                this.fetchResult.setText((CharSequence)"\u0421\u043d\u0430\u0447\u0430\u043b\u0430 \u0432\u0441\u0442\u0430\u0432\u044c\u0442\u0435 \u0441\u0441\u044b\u043b\u043a\u0443.");
            }
            return;
        }
        object = ((Design.Layer)object).url.trim();
        if (this.fetchResult != null) {
            this.fetchResult.setText((CharSequence)"\u0417\u0430\u0433\u0440\u0443\u0436\u0430\u044e\u2026");
        }
        new Thread(new MainActivity$$ExternalSyntheticLambda93(this, (String)object)).start();
    }

    private LinearLayout.LayoutParams titleLp() {
        LinearLayout.LayoutParams layoutParams = this.ui.match();
        layoutParams.topMargin = this.ui.dp(14);
        layoutParams.leftMargin = this.ui.dp(6);
        return layoutParams;
    }

    private void toast(String string2) {
        Toast.makeText((Context)this, (CharSequence)string2, (int)1).show();
    }

    private void updateNav() {
        for (int i = 0; i < this.nav.getChildCount(); ++i) {
            Object object = (LinearLayout)this.nav.getChildAt(i);
            int n = 0;
            TextView textView = (TextView)object.getChildAt(0);
            TextView textView2 = (TextView)object.getChildAt(1);
            if (i == this.tab) {
                n = 1;
            }
            object = n != 0 ? this.ui.rr(1430163967, 16) : null;
            textView.setBackground((Drawable)object);
            n = n != 0 ? -723722 : -6512986;
            textView2.setTextColor(n);
        }
    }

    private String weatherSummary() {
        long l = System.currentTimeMillis();
        long l2 = this.sp.getLong("weatherAt", 0L);
        if (this.design.weatherCity.isEmpty()) {
            return "\u0413\u043e\u0440\u043e\u0434 \u043d\u0435 \u0432\u044b\u0431\u0440\u0430\u043d.";
        }
        if (l - l2 <= 14400000L && this.sp.contains("weatherCode")) {
            return this.design.weatherCity + ": " + Store.weatherName(this.sp.getInt("weatherCode", -1)) + String.format(Locale.US, ", %+.0f\u00b0", Float.valueOf(this.sp.getFloat("weatherTemp", 0.0f)));
        }
        return this.design.weatherCity + ": \u043f\u043e\u0433\u043e\u0434\u0430 \u0435\u0449\u0451 \u043d\u0435 \u043f\u043e\u043b\u0443\u0447\u0435\u043d\u0430.";
    }

    protected void onActivityResult(int n, int n2, Intent intent) {
        super.onActivityResult(n, n2, intent);
        if (n2 == -1 && intent != null && intent.getData() != null) {
            intent = intent.getData();
            this.toast("\u041e\u0431\u0440\u0430\u0431\u0430\u0442\u044b\u0432\u0430\u044e\u2026");
            new Thread(new MainActivity$$ExternalSyntheticLambda49(this, n, (Uri)intent)).start();
            return;
        }
    }

    protected void onCreate(Bundle bundle) {
        super.onCreate(bundle);
        Fonts.init((Context)this);
        this.sp = Store.prefs((Context)this);
        this.ui = new Ui((Context)this);
        this.design = Design.load((Context)this);
        this.getWindow().setStatusBarColor(-16119284);
        this.getWindow().setNavigationBarColor(-16119284);
        bundle = new LinearLayout((Context)this);
        bundle.setOrientation(1);
        bundle.setBackgroundColor(-16119284);
        LinearLayout linearLayout = new LinearLayout((Context)this);
        linearLayout.setOrientation(0);
        linearLayout.setGravity(16);
        linearLayout.setPadding(this.ui.dp(20), this.ui.dp(10), this.ui.dp(16), this.ui.dp(4));
        TextView textView = this.ui.label("\u042d\u043a\u0440\u0430\u043d \u0431\u043b\u043e\u043a\u0438\u0440\u043e\u0432\u043a\u0438", 19.0f, -723722, true);
        textView.setSingleLine(true);
        textView.setEllipsize(TextUtils.TruncateAt.END);
        linearLayout.addView((View)textView, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        linearLayout.addView((View)this.ui.pill("\u2922", false, new MainActivity$$ExternalSyntheticLambda30(this)));
        linearLayout.addView(this.ui.space(8));
        linearLayout.addView((View)this.ui.pill("\u0423\u0441\u0442\u0430\u043d\u043e\u0432\u0438\u0442\u044c", true, new MainActivity$$ExternalSyntheticLambda31(this)));
        bundle.addView((View)linearLayout);
        int n = Math.round((float)this.getResources().getDisplayMetrics().heightPixels * 0.28f);
        int n2 = Math.round((float)n / 2.1666667f);
        this.preview = new PreviewView((Context)this);
        this.preview.interactive = true;
        this.preview.design = this.design;
        this.preview.selected = this.selectedId;
        this.preview.listener = new PreviewView.Listener(this){
            final MainActivity this$0;
            {
                this.this$0 = mainActivity;
            }

            @Override
            public void onMoved() {
                this.this$0.refreshMinis();
                if (this.this$0.tab == 3) {
                    this.this$0.rebuild();
                }
            }

            @Override
            public void onSelect(String string2) {
                if (string2 != null && !string2.equals(this.this$0.selectedId)) {
                    this.this$0.selectedId = string2;
                    if (this.this$0.tab == 3) {
                        this.this$0.rebuild();
                    }
                }
            }
        };
        textView = new LinearLayout.LayoutParams(n2, n);
        textView.gravity = 1;
        textView.setMargins(0, this.ui.dp(6), 0, this.ui.dp(4));
        bundle.addView((View)this.preview, (ViewGroup.LayoutParams)textView);
        textView = this.ui.label("\u0414\u0432\u0438\u0433\u0430\u0439\u0442\u0435 \u043f\u0430\u043b\u044c\u0446\u0435\u043c \u00b7 \u0449\u0438\u043f\u043e\u043a \u2014 \u0440\u0430\u0437\u043c\u0435\u0440 \u00b7 \u0434\u0432\u0430 \u043f\u0430\u043b\u044c\u0446\u0430 \u2014 \u043f\u043e\u0432\u043e\u0440\u043e\u0442", 12.0f, -6512986, false);
        textView.setGravity(17);
        bundle.addView((View)textView);
        this.scroll = new ScrollView((Context)this);
        this.scroll.setVerticalScrollBarEnabled(false);
        this.content = new LinearLayout((Context)this);
        this.content.setOrientation(1);
        this.content.setPadding(this.ui.dp(14), this.ui.dp(10), this.ui.dp(14), this.ui.dp(16));
        this.scroll.addView((View)this.content, new ViewGroup.LayoutParams(-1, -2));
        bundle.addView((View)this.scroll, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, 0, 1.0f));
        bundle.addView(this.buildNav());
        this.setContentView((View)bundle);
        this.showTab(0);
    }

    protected void onResume() {
        super.onResume();
        if (this.preview == null) {
            return;
        }
        if (this.fromEditor) {
            this.fromEditor = false;
            this.preview.design = this.design = Design.load((Context)this);
            this.rebuild();
        }
        this.preview.invalidate();
    }
}

