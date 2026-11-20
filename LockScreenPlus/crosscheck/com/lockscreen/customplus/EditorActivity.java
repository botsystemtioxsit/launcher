/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.app.Activity
 *  android.content.Context
 *  android.os.Bundle
 *  android.view.View
 *  android.view.ViewGroup$LayoutParams
 *  android.widget.LinearLayout
 *  android.widget.LinearLayout$LayoutParams
 *  android.widget.ScrollView
 */
package com.lockscreen.customplus;

import android.app.Activity;
import android.content.Context;
import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import com.lockscreen.customplus.Design;
import com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda0;
import com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda1;
import com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda2;
import com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda3;
import com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda4;
import com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda5;
import com.lockscreen.customplus.EditorActivity$$ExternalSyntheticLambda6;
import com.lockscreen.customplus.Fonts;
import com.lockscreen.customplus.PreviewView;
import com.lockscreen.customplus.Ui;
import java.util.List;

public class EditorActivity
extends Activity {
    private Design design;
    private LinearLayout panel;
    private PreviewView preview;
    private String selectedId = "clock";
    private Ui ui;

    public static /* synthetic */ void $r8$lambda$AnlpSxRYIQoimv785rmw8QzH5yo(EditorActivity editorActivity, Design.Xf xf, int n) {
        editorActivity.lambda$buildPanel$3(xf, n);
    }

    public static /* synthetic */ void $r8$lambda$TEUQvsazG8_VyZSX_MlrxbMVNoU(EditorActivity editorActivity, Design.Xf xf, int n) {
        editorActivity.lambda$buildPanel$4(xf, n);
    }

    public static /* synthetic */ void $r8$lambda$bmoRXkm2p6oAaDe1saCNQWIzmYY(EditorActivity editorActivity, Design.Xf xf) {
        editorActivity.lambda$buildPanel$5(xf);
    }

    public static /* synthetic */ void $r8$lambda$cwB7h0_CaN91rzONuwaRe8ShCwM(EditorActivity editorActivity) {
        editorActivity.finish();
    }

    public static /* synthetic */ void $r8$lambda$gAgtVkdKL_Ht3Oav3kLKewsUeu8(EditorActivity editorActivity, Design.Xf xf, int n) {
        editorActivity.lambda$buildPanel$2(xf, n);
    }

    public static /* synthetic */ void $r8$lambda$lJvfkdy_0__Puevbi7f61bw_wgI(EditorActivity editorActivity, Design.Xf xf, int n) {
        editorActivity.lambda$buildPanel$1(xf, n);
    }

    public static /* synthetic */ void $r8$lambda$rAik08Dfqh0IKohciSYrY_fW92s(EditorActivity editorActivity, List list, int n) {
        editorActivity.lambda$buildPanel$0(list, n);
    }

    private void buildPanel() {
        this.panel.removeAllViews();
        if (this.design.pos(this.selectedId) == null) {
            this.selectedId = "clock";
        }
        this.preview.selected = this.selectedId;
        Object object = this.selectedId;
        LinearLayout linearLayout = this.ui.card(this.panel, null);
        List<String[]> list = this.design.elements();
        String[] stringArray = new String[list.size()];
        int n = 0;
        for (int i = 0; i < list.size(); ++i) {
            stringArray[i] = list.get(i)[1];
            if (!list.get(i)[0].equals(object)) continue;
            n = i;
        }
        linearLayout.addView(this.ui.chips(stringArray, n, new EditorActivity$$ExternalSyntheticLambda0(this, list)));
        object = this.design.xf((String)object);
        linearLayout.addView(this.ui.slider("\u041d\u0430\u043a\u043b\u043e\u043d \u0432\u043f\u0435\u0440\u0451\u0434-\u043d\u0430\u0437\u0430\u0434", -80, 80, ((Design.Xf)object).rx, "\u00b0", new EditorActivity$$ExternalSyntheticLambda1(this, (Design.Xf)object)));
        linearLayout.addView(this.ui.slider("\u041d\u0430\u043a\u043b\u043e\u043d \u0432\u043b\u0435\u0432\u043e-\u0432\u043f\u0440\u0430\u0432\u043e", -80, 80, ((Design.Xf)object).ry, "\u00b0", new EditorActivity$$ExternalSyntheticLambda2(this, (Design.Xf)object)));
        linearLayout.addView(this.ui.slider("\u041f\u0435\u0440\u0435\u043a\u043e\u0441", -60, 60, ((Design.Xf)object).skew, "", new EditorActivity$$ExternalSyntheticLambda3(this, (Design.Xf)object)));
        linearLayout.addView(this.ui.slider("\u041f\u043e\u0432\u043e\u0440\u043e\u0442", -180, 180, ((Design.Xf)object).rot, "\u00b0", new EditorActivity$$ExternalSyntheticLambda4(this, (Design.Xf)object)));
        linearLayout.addView((View)this.ui.pill("\u0421\u0431\u0440\u043e\u0441\u0438\u0442\u044c \u043f\u043e\u0432\u043e\u0440\u043e\u0442 \u0438 \u043d\u0430\u043a\u043b\u043e\u043d", false, new EditorActivity$$ExternalSyntheticLambda5(this, (Design.Xf)object)));
    }

    private void commit() {
        this.design.save((Context)this);
        this.preview.invalidate();
    }

    private /* synthetic */ void lambda$buildPanel$0(List list, int n) {
        this.selectedId = ((String[])list.get(n))[0];
        this.buildPanel();
        this.preview.invalidate();
    }

    private /* synthetic */ void lambda$buildPanel$1(Design.Xf xf, int n) {
        xf.rx = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildPanel$2(Design.Xf xf, int n) {
        xf.ry = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildPanel$3(Design.Xf xf, int n) {
        xf.skew = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildPanel$4(Design.Xf xf, int n) {
        xf.rot = n;
        this.commit();
    }

    private /* synthetic */ void lambda$buildPanel$5(Design.Xf xf) {
        xf.reset();
        this.commit();
        this.buildPanel();
    }

    protected void onCreate(Bundle object) {
        int n;
        super.onCreate(object);
        Fonts.init((Context)this);
        this.ui = new Ui((Context)this);
        this.design = Design.load((Context)this);
        object = this.getIntent().getStringExtra("selected");
        if (object != null && this.design.pos((String)object) != null) {
            this.selectedId = object;
        }
        this.getWindow().setStatusBarColor(-16119284);
        this.getWindow().setNavigationBarColor(-16119284);
        object = new LinearLayout((Context)this);
        object.setOrientation(1);
        object.setBackgroundColor(-16119284);
        LinearLayout linearLayout = this.ui.hrow();
        linearLayout.setPadding(this.ui.dp(14), this.ui.dp(8), this.ui.dp(14), this.ui.dp(4));
        linearLayout.addView((View)this.ui.pill("\u2715 \u0413\u043e\u0442\u043e\u0432\u043e", true, new EditorActivity$$ExternalSyntheticLambda6(this)), (ViewGroup.LayoutParams)this.ui.wrap());
        linearLayout.addView((View)this.ui.label("  \u0414\u0432\u0438\u0433\u0430\u0439\u0442\u0435 \u00b7 \u0449\u0438\u043f\u043e\u043a \u2014 \u0440\u0430\u0437\u043c\u0435\u0440 \u00b7 \u0434\u0432\u0430 \u043f\u0430\u043b\u044c\u0446\u0430 \u2014 \u043f\u043e\u0432\u043e\u0440\u043e\u0442", 12.0f, -6512986, false), (ViewGroup.LayoutParams)this.ui.wrap());
        object.addView((View)linearLayout);
        int n2 = this.getResources().getDisplayMetrics().widthPixels;
        int n3 = Math.round((float)this.getResources().getDisplayMetrics().heightPixels * 0.58f);
        int n4 = n = Math.round((float)n3 / 2.1666667f);
        if ((float)n > (float)n2 * 0.92f) {
            n4 = Math.round((float)n2 * 0.92f);
            n3 = Math.round((float)n4 * 2.1666667f);
        }
        this.preview = new PreviewView((Context)this);
        this.preview.interactive = true;
        this.preview.design = this.design;
        this.preview.selected = this.selectedId;
        this.preview.listener = new PreviewView.Listener(this){
            final EditorActivity this$0;
            {
                this.this$0 = editorActivity;
            }

            @Override
            public void onMoved() {
                this.this$0.buildPanel();
            }

            @Override
            public void onSelect(String string2) {
                if (string2 != null && !string2.equals(this.this$0.selectedId)) {
                    this.this$0.selectedId = string2;
                    this.this$0.buildPanel();
                }
            }
        };
        linearLayout = new LinearLayout.LayoutParams(n4, n3);
        linearLayout.gravity = 1;
        linearLayout.setMargins(0, this.ui.dp(4), 0, this.ui.dp(6));
        object.addView((View)this.preview, (ViewGroup.LayoutParams)linearLayout);
        linearLayout = new ScrollView((Context)this);
        this.panel = new LinearLayout((Context)this);
        this.panel.setOrientation(1);
        this.panel.setPadding(this.ui.dp(14), 0, this.ui.dp(14), this.ui.dp(12));
        linearLayout.addView((View)this.panel, new ViewGroup.LayoutParams(-1, -2));
        object.addView((View)linearLayout, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, 0, 1.0f));
        this.setContentView((View)object);
        this.buildPanel();
    }
}

