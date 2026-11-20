/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.content.Context
 *  android.content.res.ColorStateList
 *  android.graphics.drawable.Drawable
 *  android.graphics.drawable.GradientDrawable
 *  android.text.TextWatcher
 *  android.view.View
 *  android.view.View$OnClickListener
 *  android.view.ViewGroup$LayoutParams
 *  android.widget.CompoundButton
 *  android.widget.CompoundButton$OnCheckedChangeListener
 *  android.widget.EditText
 *  android.widget.HorizontalScrollView
 *  android.widget.LinearLayout
 *  android.widget.LinearLayout$LayoutParams
 *  android.widget.SeekBar
 *  android.widget.SeekBar$OnSeekBarChangeListener
 *  android.widget.Switch
 *  android.widget.TextView
 */
package com.lockscreen.customplus;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.text.TextWatcher;
import android.view.View;
import android.view.ViewGroup;
import android.widget.CompoundButton;
import android.widget.EditText;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.SeekBar;
import android.widget.Switch;
import android.widget.TextView;
import com.lockscreen.customplus.Ui$$ExternalSyntheticLambda0;
import com.lockscreen.customplus.Ui$$ExternalSyntheticLambda1;
import com.lockscreen.customplus.Ui$$ExternalSyntheticLambda2;
import com.lockscreen.customplus.Ui$$ExternalSyntheticLambda3;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Consumer;
import java.util.function.IntConsumer;

final class Ui {
    static final int ACCENT = -12676609;
    static final int BG = -16119284;
    static final int CARD = -14935008;
    static final int CHIP = -14013647;
    static final int SUB = -6512986;
    static final int TEXT = -723722;
    final Context ctx;

    public static /* synthetic */ void $r8$lambda$KnA8OH7ghIjqB6nk44q3fvepdS8(Ui ui, List list, int[] nArray, int n, IntConsumer intConsumer, View view) {
        ui.lambda$swatches$3(list, nArray, n, intConsumer, view);
    }

    public static /* synthetic */ void $r8$lambda$SJg5eq57lNs4iRj80RIr0lp3dS4(Ui ui, List list, int n, IntConsumer intConsumer, View view) {
        ui.lambda$chips$2(list, n, intConsumer, view);
    }

    Ui(Context context) {
        this.ctx = context;
    }

    private /* synthetic */ void lambda$chips$2(List list, int n, IntConsumer intConsumer, View view) {
        this.styleChips(list, n);
        intConsumer.accept(n);
    }

    static /* synthetic */ void lambda$pill$0(Runnable runnable2, View view) {
        runnable2.run();
    }

    static /* synthetic */ void lambda$sw$1(Consumer consumer, CompoundButton compoundButton, boolean bl) {
        consumer.accept(bl);
    }

    private /* synthetic */ void lambda$swatches$3(List list, int[] nArray, int n, IntConsumer intConsumer, View view) {
        this.styleSwatches(list, nArray, n);
        intConsumer.accept(n);
    }

    private void styleSwatches(List<View> list, int[] nArray, int n) {
        for (int i = 0; i < list.size(); ++i) {
            GradientDrawable gradientDrawable = new GradientDrawable();
            int n2 = 1;
            gradientDrawable.setShape(1);
            gradientDrawable.setColor(nArray[i]);
            int n3 = nArray[i] == n ? 1 : 0;
            if (n3 != 0) {
                n2 = 3;
            }
            n2 = this.dp(n2);
            n3 = n3 != 0 ? -12676609 : -10855581;
            gradientDrawable.setStroke(n2, n3);
            list.get(i).setBackground((Drawable)gradientDrawable);
        }
    }

    LinearLayout card(LinearLayout linearLayout, String string2) {
        LinearLayout linearLayout2 = new LinearLayout(this.ctx);
        linearLayout2.setOrientation(1);
        linearLayout2.setPadding(this.dp(16), this.dp(12), this.dp(16), this.dp(14));
        linearLayout2.setBackground((Drawable)this.rr(-14935008, 26));
        if (string2 != null) {
            linearLayout2.addView((View)this.label(string2, 16.0f, -723722, true));
        }
        string2 = this.match();
        ((LinearLayout.LayoutParams)string2).topMargin = this.dp(10);
        linearLayout.addView((View)linearLayout2, (ViewGroup.LayoutParams)string2);
        return linearLayout2;
    }

    View chips(String[] stringArray, int n, IntConsumer intConsumer) {
        HorizontalScrollView horizontalScrollView = new HorizontalScrollView(this.ctx);
        horizontalScrollView.setHorizontalScrollBarEnabled(false);
        LinearLayout linearLayout = this.hrow();
        ArrayList<TextView> arrayList = new ArrayList<TextView>();
        for (int i = 0; i < stringArray.length; ++i) {
            TextView textView = this.label(stringArray[i], 14.0f, -723722, false);
            textView.setPadding(this.dp(16), this.dp(9), this.dp(16), this.dp(9));
            arrayList.add(textView);
            textView.setOnClickListener((View.OnClickListener)new Ui$$ExternalSyntheticLambda2(this, arrayList, i, intConsumer));
            LinearLayout.LayoutParams layoutParams = this.wrap();
            layoutParams.rightMargin = this.dp(6);
            layoutParams.topMargin = this.dp(4);
            layoutParams.bottomMargin = this.dp(4);
            linearLayout.addView((View)textView, (ViewGroup.LayoutParams)layoutParams);
        }
        this.styleChips(arrayList, n);
        horizontalScrollView.addView((View)linearLayout);
        return horizontalScrollView;
    }

    int dp(int n) {
        return Math.round((float)n * this.ctx.getResources().getDisplayMetrics().density);
    }

    EditText edit(String string2, boolean bl) {
        EditText editText = new EditText(this.ctx);
        editText.setHint((CharSequence)string2);
        editText.setTextColor(-723722);
        editText.setHintTextColor(-9539464);
        editText.setTextSize(2, 15.0f);
        editText.setBackground((Drawable)this.rr(-14013647, 16));
        editText.setPadding(this.dp(14), this.dp(10), this.dp(14), this.dp(10));
        if (bl) {
            editText.setInputType(131073);
            editText.setMinLines(2);
            editText.setGravity(48);
        } else {
            editText.setInputType(17);
            editText.setSingleLine(true);
        }
        string2 = this.match();
        ((LinearLayout.LayoutParams)string2).topMargin = this.dp(6);
        ((LinearLayout.LayoutParams)string2).bottomMargin = this.dp(6);
        editText.setLayoutParams((ViewGroup.LayoutParams)string2);
        return editText;
    }

    LinearLayout hrow() {
        LinearLayout linearLayout = new LinearLayout(this.ctx);
        linearLayout.setOrientation(0);
        linearLayout.setGravity(16);
        return linearLayout;
    }

    TextView label(String string2, float f, int n, boolean bl) {
        TextView textView = new TextView(this.ctx);
        textView.setText((CharSequence)string2);
        textView.setTextSize(2, f);
        textView.setTextColor(n);
        if (bl) {
            textView.setTypeface(textView.getTypeface(), 1);
        }
        textView.setPadding(0, this.dp(4), 0, this.dp(2));
        return textView;
    }

    LinearLayout.LayoutParams match() {
        return new LinearLayout.LayoutParams(-1, -2);
    }

    TextView pill(String string2, boolean bl, Runnable runnable2) {
        int n = bl ? -1 : -723722;
        string2 = this.label(string2, 14.0f, n, true);
        string2.setGravity(17);
        string2.setPadding(this.dp(16), this.dp(10), this.dp(16), this.dp(10));
        n = bl ? -12676609 : -14013647;
        string2.setBackground((Drawable)this.rr(n, 22));
        string2.setOnClickListener((View.OnClickListener)new Ui$$ExternalSyntheticLambda0(runnable2));
        return string2;
    }

    GradientDrawable rr(int n, int n2) {
        GradientDrawable gradientDrawable = new GradientDrawable();
        gradientDrawable.setColor(n);
        gradientDrawable.setCornerRadius((float)this.dp(n2));
        return gradientDrawable;
    }

    View slider(String string2, int n, int n2, int n3, String string3, IntConsumer intConsumer) {
        LinearLayout linearLayout = new LinearLayout(this.ctx);
        linearLayout.setOrientation(1);
        LinearLayout linearLayout2 = this.hrow();
        TextView textView = this.label(string2, 14.0f, -723722, false);
        string2 = this.label(Math.max(n, Math.min(n2, n3)) + string3, 14.0f, -6512986, false);
        linearLayout2.addView((View)textView, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        linearLayout2.addView((View)string2);
        textView = new SeekBar(this.ctx);
        textView.setMax(n2 - n);
        textView.setProgress(Math.max(0, Math.min(n2, n3) - n));
        textView.setProgressTintList(ColorStateList.valueOf((int)-12676609));
        textView.setThumbTintList(ColorStateList.valueOf((int)-1));
        textView.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){
            final Ui this$0;
            final int val$min;
            final IntConsumer val$onChange;
            final String val$unit;
            final TextView val$v;
            {
                this.this$0 = ui;
                this.val$v = textView;
                this.val$min = n;
                this.val$unit = string2;
                this.val$onChange = intConsumer;
            }

            public void onProgressChanged(SeekBar seekBar, int n, boolean bl) {
                this.val$v.setText((CharSequence)(this.val$min + n + this.val$unit));
                if (bl) {
                    this.val$onChange.accept(this.val$min + n);
                }
            }

            public void onStartTrackingTouch(SeekBar seekBar) {
            }

            public void onStopTrackingTouch(SeekBar seekBar) {
            }
        });
        linearLayout.addView((View)linearLayout2);
        linearLayout.addView((View)textView);
        return linearLayout;
    }

    View space(int n) {
        View view = new View(this.ctx);
        view.setLayoutParams((ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(n), 1));
        return view;
    }

    void styleChips(List<TextView> list, int n) {
        for (int i = 0; i < list.size(); ++i) {
            TextView textView = list.get(i);
            int n2 = i == n ? 1 : 0;
            int n3 = n2 != 0 ? -12676609 : -14013647;
            textView.setBackground((Drawable)this.rr(n3, 20));
            n2 = n2 != 0 ? -1 : -723722;
            textView.setTextColor(n2);
        }
    }

    View sw(String object, boolean bl, Consumer<Boolean> consumer) {
        Switch switch_ = new Switch(this.ctx);
        switch_.setText((CharSequence)object);
        switch_.setTextColor(-723722);
        switch_.setTextSize(2, 15.0f);
        switch_.setChecked(bl);
        switch_.setPadding(0, this.dp(8), 0, this.dp(8));
        object = new int[2][];
        object[0] = new int[]{0x10100A0};
        object[1] = new int[0];
        switch_.setThumbTintList(new ColorStateList((int[][])object, new int[]{-1, -5197126}));
        switch_.setTrackTintList(new ColorStateList((int[][])object, new int[]{-12676609, -12960958}));
        switch_.setOnCheckedChangeListener((CompoundButton.OnCheckedChangeListener)new Ui$$ExternalSyntheticLambda1(consumer));
        return switch_;
    }

    View swatches(int[] nArray, int n, IntConsumer intConsumer) {
        HorizontalScrollView horizontalScrollView = new HorizontalScrollView(this.ctx);
        horizontalScrollView.setHorizontalScrollBarEnabled(false);
        LinearLayout linearLayout = this.hrow();
        ArrayList<View> arrayList = new ArrayList<View>();
        for (int i = 0; i < nArray.length; ++i) {
            int n2 = nArray[i];
            View view = new View(this.ctx);
            arrayList.add(view);
            view.setOnClickListener((View.OnClickListener)new Ui$$ExternalSyntheticLambda3(this, arrayList, nArray, n2, intConsumer));
            LinearLayout.LayoutParams layoutParams = new LinearLayout.LayoutParams(this.dp(38), this.dp(38));
            layoutParams.rightMargin = this.dp(8);
            layoutParams.topMargin = this.dp(6);
            layoutParams.bottomMargin = this.dp(6);
            linearLayout.addView(view, (ViewGroup.LayoutParams)layoutParams);
        }
        this.styleSwatches(arrayList, nArray, n);
        horizontalScrollView.addView((View)linearLayout);
        return horizontalScrollView;
    }

    LinearLayout.LayoutParams wrap() {
        return new LinearLayout.LayoutParams(-2, -2);
    }

    static abstract class SimpleWatcher
    implements TextWatcher {
        SimpleWatcher() {
        }

        public void beforeTextChanged(CharSequence charSequence, int n, int n2, int n3) {
        }

        public void onTextChanged(CharSequence charSequence, int n, int n2, int n3) {
        }
    }
}

