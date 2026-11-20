package com.lockscreen.customplus;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** После перезагрузки телефона снова включает слежение для «Режима часов». */
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context c, Intent i) {
        try {
            ClockModeService.apply(c);
        } catch (Exception e) {
        }
    }
}
