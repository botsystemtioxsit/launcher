/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.widget.CompoundButton
 *  android.widget.CompoundButton$OnCheckedChangeListener
 */
package com.lockscreen.customplus;

import android.widget.CompoundButton;
import com.lockscreen.customplus.Ui;
import java.util.function.Consumer;

public final class Ui$$ExternalSyntheticLambda1
implements CompoundButton.OnCheckedChangeListener {
    public final Consumer f$0;

    public /* synthetic */ Ui$$ExternalSyntheticLambda1(Consumer consumer) {
        this.f$0 = consumer;
    }

    public final void onCheckedChanged(CompoundButton compoundButton, boolean bl) {
        Ui.lambda$sw$1(this.f$0, compoundButton, bl);
    }
}

