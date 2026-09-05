package com.arashi.lunwu;

import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Build;
import android.provider.Settings;

import java.util.Locale;

final class DeviceAdapter {
    static final class Profile {
        final String name;
        final String[] optimizerPackages;
        final String[] gamePackages;
        Profile(String name, String[] optimizerPackages, String[] gamePackages) {
            this.name = name;
            this.optimizerPackages = optimizerPackages;
            this.gamePackages = gamePackages;
        }
    }

    private DeviceAdapter() {}

    static Profile detect() {
        String key = (Build.MANUFACTURER + " " + Build.BRAND).toLowerCase(Locale.ROOT);
        if (key.contains("xiaomi") || key.contains("redmi") || key.contains("poco")) {
            return new Profile("HyperOS / MIUI",
                    new String[]{"com.miui.securitycenter"},
                    new String[]{"com.miui.securitycenter"});
        }
        if (key.contains("samsung")) {
            return new Profile("Samsung One UI",
                    new String[]{"com.samsung.android.lool"},
                    new String[]{"com.samsung.android.game.gametools", "com.samsung.android.game.gamehome"});
        }
        if (key.contains("huawei")) {
            return new Profile("Huawei EMUI / HarmonyOS",
                    new String[]{"com.huawei.systemmanager"},
                    new String[]{"com.huawei.gameassistant"});
        }
        if (key.contains("honor")) {
            return new Profile("Honor MagicOS",
                    new String[]{"com.hhihonor.systemmanager", "com.hihonor.systemmanager"},
                    new String[]{"com.hhihonor.gameback", "com.hihonor.gamecenter"});
        }
        if (key.contains("oppo") || key.contains("oplus") || key.contains("oneplus") || key.contains("realme")) {
            return new Profile("ColorOS / OxygenOS / realme UI",
                    new String[]{"com.oplus.phonemanager", "com.coloros.phonemanager", "com.coloros.safecenter", "com.oplus.battery"},
                    new String[]{"com.oplus.games", "com.coloros.gamespace"});
        }
        if (key.contains("vivo") || key.contains("iqoo")) {
            return new Profile("vivo / iQOO OriginOS",
                    new String[]{"com.iqoo.secure", "com.vivo.security"},
                    new String[]{"com.vivo.game", "com.vivo.gamewatch"});
        }
        if (key.contains("meizu")) {
            return new Profile("Meizu Flyme", new String[]{"com.meizu.safe"}, new String[0]);
        }
        if (key.contains("asus")) {
            return new Profile("ASUS / ROG ZenUI", new String[]{"com.asus.mobilemanager"}, new String[0]);
        }
        return new Profile("Standard Android", new String[0], new String[0]);
    }

    static boolean isInstalled(Context c, String pkg) {
        try { c.getPackageManager().getApplicationInfo(pkg, 0); return true; }
        catch (Throwable t) { return false; }
    }

    static boolean openOptimizer(Context c) {
        Profile p = detect();
        if (openFirstInstalled(c, p.optimizerPackages)) return true;
        try {
            Intent i = new Intent(Settings.ACTION_BATTERY_SAVER_SETTINGS);
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            c.startActivity(i);
            return true;
        } catch (Throwable t) {
            try { c.startActivity(new Intent(Settings.ACTION_SETTINGS)); return true; }
            catch (Throwable ignored) { return false; }
        }
    }

    static boolean openGameMode(Context c) {
        Profile p = detect();
        if (openFirstInstalled(c, p.gamePackages)) return true;
        return openOptimizer(c);
    }

    private static boolean openFirstInstalled(Context c, String[] packages) {
        PackageManager pm = c.getPackageManager();
        for (String pkg : packages) {
            try {
                Intent i = pm.getLaunchIntentForPackage(pkg);
                if (i != null) {
                    i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    c.startActivity(i);
                    return true;
                }
            } catch (Throwable ignored) {}
        }
        return false;
    }
}
