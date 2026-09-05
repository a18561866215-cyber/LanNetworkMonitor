package com.arashi.lunwu;

import android.app.Activity;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.ServiceConnection;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.os.Bundle;
import android.os.IBinder;
import android.os.RemoteException;
import android.os.UserHandle;
import android.provider.Settings;
import android.view.MotionEvent;
import android.view.View;
import android.view.Window;
import android.view.WindowInsetsController;
import android.view.WindowManager;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import rikka.shizuku.Shizuku;

public class MainActivity extends Activity {
    private static final int REQ = 4001;
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private LunWuView view;
    private volatile IRemoteShell shell;
    private Shizuku.UserServiceArgs serviceArgs;

    private final Shizuku.OnBinderReceivedListener binderReceived = () -> runOnUiThread(this::ensureShizuku);
    private final Shizuku.OnBinderDeadListener binderDead = () -> runOnUiThread(() -> {
        shell = null;
        if (view != null) view.setConnection("Shizuku 已断开", false);
    });
    private final Shizuku.OnRequestPermissionResultListener permissionResult = (requestCode, grantResult) -> {
        if (requestCode != REQ) return;
        runOnUiThread(() -> {
            if (grantResult == PackageManager.PERMISSION_GRANTED) bindShell();
            else view.setConnection("需要 Shizuku 授权", false);
        });
    };

    private final ServiceConnection connection = new ServiceConnection() {
        @Override public void onServiceConnected(ComponentName name, IBinder service) {
            shell = IRemoteShell.Stub.asInterface(service);
            runOnUiThread(() -> {
                view.setConnection("Shizuku · READY", true);
                scan();
            });
        }
        @Override public void onServiceDisconnected(ComponentName name) {
            shell = null;
            runOnUiThread(() -> view.setConnection("Shell 已断开", false));
        }
    };

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        Window w = getWindow();
        w.setStatusBarColor(Color.rgb(13,14,11));
        w.setNavigationBarColor(Color.rgb(13,14,11));
        if (android.os.Build.VERSION.SDK_INT >= 30) {
            WindowInsetsController c = w.getInsetsController();
            if (c != null) c.setSystemBarsAppearance(0,
                    WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS |
                    WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS);
        } else w.addFlags(WindowManager.LayoutParams.FLAG_DRAWS_SYSTEM_BAR_BACKGROUNDS);
        view = new LunWuView(this);
        setContentView(view);
        Shizuku.addBinderReceivedListener(binderReceived, true);
        Shizuku.addBinderDeadListener(binderDead);
        Shizuku.addRequestPermissionResultListener(permissionResult);
    }

    @Override protected void onResume() {
        super.onResume();
        ensureShizuku();
    }

    @Override protected void onDestroy() {
        Shizuku.removeBinderReceivedListener(binderReceived);
        Shizuku.removeBinderDeadListener(binderDead);
        Shizuku.removeRequestPermissionResultListener(permissionResult);
        worker.shutdownNow();
        super.onDestroy();
    }

    private void ensureShizuku() {
        if (!Shizuku.pingBinder()) {
            shell = null;
            view.setConnection("请先启动 Shizuku", false);
            return;
        }
        try {
            if (Shizuku.checkSelfPermission() == PackageManager.PERMISSION_GRANTED) {
                if (shell == null) bindShell();
            } else {
                view.setConnection("点击授权 Shizuku", false);
                Shizuku.requestPermission(REQ);
            }
        } catch (Throwable t) {
            view.setConnection("等待 Shizuku Binder…", false);
        }
    }

    private void bindShell() {
        if (!Shizuku.pingBinder()) return;
        if (serviceArgs == null) {
            serviceArgs = new Shizuku.UserServiceArgs(new ComponentName(this, ShizukuShellService.class))
                    .processNameSuffix("lunwu_shell").daemon(false).debuggable(false).version(1);
        }
        try {
            Shizuku.bindUserService(serviceArgs, connection);
            view.setConnection("正在连接 Shell…", false);
        } catch (Throwable t) {
            view.setConnection("连接失败", false);
        }
    }

    void openShizuku() {
        Intent i = getPackageManager().getLaunchIntentForPackage("moe.shizuku.privileged.api");
        if (i != null) startActivity(i);
        else try { startActivity(new Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS)); }
        catch (Throwable ignored) {}
    }

    void scan() {
        IRemoteShell s = shell;
        if (s == null) { ensureShizuku(); return; }
        view.scanning = true;
        view.message = "正在读取真实进程表…";
        view.invalidate();
        worker.execute(() -> {
            try {
                Set<String> names = parse(s.exec("ps -A -o NAME= 2>/dev/null || ps -A 2>/dev/null"));
                List<AppRow> rows = collect(names);
                runOnUiThread(() -> view.setApps(rows));
            } catch (Throwable t) {
                runOnUiThread(() -> {
                    view.scanning = false;
                    view.message = "扫描失败 · " + t.getClass().getSimpleName();
                    view.invalidate();
                });
            }
        });
    }

    private Set<String> parse(String out) {
        Set<String> names = new HashSet<>();
        if (out == null) return names;
        for (String raw : out.split("\\r?\\n")) {
            String line = raw.trim();
            if (line.isEmpty() || line.startsWith("__LUNWU_") || line.equals("NAME")) continue;
            String[] bits = line.split("\\s+");
            names.add(bits[bits.length - 1]);
        }
        return names;
    }

    private List<AppRow> collect(Set<String> processNames) {
        PackageManager pm = getPackageManager();
        String ime = currentIme();
        String launcher = currentLauncher();
        List<AppRow> rows = new ArrayList<>();
        for (ApplicationInfo ai : pm.getInstalledApplications(PackageManager.GET_META_DATA)) {
            if ((ai.flags & ApplicationInfo.FLAG_SYSTEM) != 0 ||
                    (ai.flags & ApplicationInfo.FLAG_UPDATED_SYSTEM_APP) != 0) continue;
            String pkg = ai.packageName;
            if (pkg.equals(getPackageName()) || pkg.equals("moe.shizuku.privileged.api")) continue;
            int count = 0;
            for (String p : processNames) if (p.equals(pkg) || p.startsWith(pkg + ":")) count++;
            if (count == 0) continue;
            String label;
            Drawable icon;
            try { label = String.valueOf(pm.getApplicationLabel(ai)); } catch (Throwable t) { label = pkg; }
            try { icon = pm.getApplicationIcon(ai); } catch (Throwable t) { icon = null; }
            boolean protect = pkg.equals(ime) || pkg.equals(launcher);
            String reason = pkg.equals(ime) ? "当前输入法" : pkg.equals(launcher) ? "当前桌面" : "";
            rows.add(new AppRow(pkg, label, icon, count, protect, reason));
        }
        Collections.sort(rows, Comparator.comparing(a -> a.label.toLowerCase(Locale.ROOT)));
        return rows;
    }

    private String currentIme() {
        try {
            String flat = Settings.Secure.getString(getContentResolver(), Settings.Secure.DEFAULT_INPUT_METHOD);
            ComponentName c = flat == null ? null : ComponentName.unflattenFromString(flat);
            return c == null ? "" : c.getPackageName();
        } catch (Throwable t) { return ""; }
    }

    private String currentLauncher() {
        try {
            Intent i = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME);
            android.content.pm.ResolveInfo r = getPackageManager().resolveActivity(i, PackageManager.MATCH_DEFAULT_ONLY);
            return r == null || r.activityInfo == null ? "" : r.activityInfo.packageName;
        } catch (Throwable t) { return ""; }
    }

    void clean(List<AppRow> targets) {
        IRemoteShell s = shell;
        if (s == null || targets.isEmpty()) return;
        view.cleaning = true;
        view.done = 0;
        view.total = targets.size();
        view.current = "准备清场";
        view.invalidate();
        worker.execute(() -> {
            int done = 0;
            int user = UserHandle.getUserHandleForUid(android.os.Process.myUid()).getIdentifier();
            for (AppRow row : targets) {
                if (row.protectedApp || !row.pkg.matches("[A-Za-z0-9_\\.]+")) continue;
                try { s.exec("am force-stop --user " + user + " " + row.pkg); }
                catch (RemoteException ignored) {}
                done++;
                int n = done;
                runOnUiThread(() -> {
                    view.done = n;
                    view.current = row.label;
                    view.invalidate();
                });
                try { Thread.sleep(100); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
            }
            try { Thread.sleep(700); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
            int killed = done;
            try {
                List<AppRow> remaining = collect(parse(s.exec("ps -A -o NAME= 2>/dev/null || ps -A 2>/dev/null")));
                runOnUiThread(() -> view.finish(remaining, killed));
            } catch (Throwable t) {
                runOnUiThread(() -> view.finish(new ArrayList<>(), killed));
            }
        });
    }

    static class AppRow {
        final String pkg, label, reason;
        final Drawable icon;
        final int processes;
        final boolean protectedApp;
        boolean selected;
        AppRow(String p, String l, Drawable i, int n, boolean protect, String r) {
            pkg=p; label=l; icon=i; processes=n; protectedApp=protect; reason=r; selected=!protect;
        }
    }

    class LunWuView extends View {
        final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        final Paint stroke = new Paint(Paint.ANTI_ALIAS_FLAG);
        final List<AppRow> apps = new ArrayList<>();
        final RectF action = new RectF(), shizuku = new RectF();
        final float d;
        String connectionText="正在等待 Shizuku…", message="扫描真实存活进程 · 点选后强制停止", current="";
        boolean connected, scanning, cleaning, dragged;
        int done, total;
        float scroll, downY, lastY;

        LunWuView(Context c) {
            super(c);
            d=getResources().getDisplayMetrics().density;
            setBackgroundColor(Color.rgb(13,14,11));
        }
        float dp(float v){return v*d;}
        void setConnection(String s, boolean ok){connectionText=s; connected=ok; invalidate();}
        void setApps(List<AppRow> r){apps.clear();apps.addAll(r);scanning=false;cleaning=false;scroll=0;message=r.isEmpty()?"没有检测到第三方后台进程":"检测到 "+r.size()+" 个第三方后台应用";invalidate();}
        void finish(List<AppRow> r,int killed){apps.clear();apps.addAll(r);cleaning=false;scroll=0;message="已处理 "+killed+" 个 · 当前仍存活 "+r.size()+" 个";invalidate();}
        void txt(Canvas c,String s,float x,float y,float size,int color,boolean bold){p.setStyle(Paint.Style.FILL);p.setColor(color);p.setTextSize(dp(size));p.setTypeface(Typeface.create("sans",bold?Typeface.BOLD:Typeface.NORMAL));c.drawText(s,x,y,p);}
        void box(Canvas c,float l,float t,float r,float b,float rad,int color){p.setStyle(Paint.Style.FILL);p.setColor(color);c.drawRoundRect(l,t,r,b,dp(rad),dp(rad),p);}
        List<AppRow> selected(){List<AppRow> r=new ArrayList<>();for(AppRow a:apps)if(a.selected&&!a.protectedApp)r.add(a);return r;}
        String cut(String s,int n){return s.length()<=n?s:s.substring(0,n-1)+"…";}
        float quoteTop(){return connected?dp(96):dp(172);}
        float listTop(){return quoteTop()+dp(92);}

        @Override protected void onDraw(Canvas c){
            super.onDraw(c);float w=getWidth(),h=getHeight();c.drawColor(Color.rgb(13,14,11));bananas(c,w,h);
            txt(c,"轮·舞",dp(22),dp(54),28,Color.rgb(245,241,221),true);
            txt(c,"PROCESS SESSION CONTROL",dp(22),dp(74),9,Color.rgb(164,166,145),true);
            float pw=dp(connected?118:150);box(c,w-dp(22)-pw,dp(34),w-dp(22),dp(68),17,connected?Color.rgb(31,65,40):Color.rgb(58,55,31));
            txt(c,connectionText,w-dp(22)-pw+dp(12),dp(56),10,connected?Color.rgb(178,243,178):Color.rgb(246,222,122),true);
            if(!connected){shizuku.set(dp(22),dp(96),w-dp(22),dp(156));box(c,shizuku.left,shizuku.top,shizuku.right,shizuku.bottom,22,Color.rgb(245,216,76));txt(c,"启动 / 授权 Shizuku",dp(40),dp(133),14,Color.rgb(24,24,18),true);}else shizuku.setEmpty();
            float qt=quoteTop();box(c,dp(22),qt,w-dp(22),qt+dp(76),24,Color.rgb(28,30,23));
            stroke.setStyle(Paint.Style.STROKE);stroke.setStrokeWidth(dp(1));stroke.setColor(Color.argb(80,245,216,76));c.drawRoundRect(dp(22),qt,w-dp(22),qt+dp(76),dp(24),dp(24),stroke);
            if(cleaning){txt(c,"感觉好像喝了烈酒一样......",dp(40),qt+dp(33),15,Color.rgb(250,244,219),true);txt(c,"正在切断不需要的后台会话",dp(40),qt+dp(56),10,Color.rgb(175,175,150),false);}else{txt(c,"后台清场",dp(40),qt+dp(32),17,Color.rgb(250,244,219),true);txt(c,message,dp(40),qt+dp(56),10,Color.rgb(175,175,150),false);}
            float lt=listTop();c.save();c.clipRect(0,lt,w,h-dp(116));float y=lt-scroll;
            if(scanning){txt(c,"SCANNING…",dp(28),y+dp(36),13,Color.rgb(245,216,76),true);txt(c,"正在匹配包名与真实进程",dp(28),y+dp(60),11,Color.rgb(160,161,143),false);}else for(AppRow a:apps){row(c,a,dp(22),y,w-dp(22),y+dp(78));y+=dp(88);}c.restore();
            box(c,dp(14),h-dp(108),w-dp(14),h-dp(14),28,Color.rgb(25,27,21));
            if(cleaning){float pr=Math.min(1f,done/(float)Math.max(1,total));txt(c,"正在清理 · "+current,dp(30),h-dp(73),12,Color.rgb(243,239,217),true);txt(c,done+" / "+total,w-dp(70),h-dp(73),11,Color.rgb(171,173,151),true);box(c,dp(30),h-dp(52),w-dp(30),h-dp(38),7,Color.rgb(52,54,43));box(c,dp(30),h-dp(52),dp(30)+(w-dp(60))*pr,h-dp(38),7,Color.rgb(245,216,76));txt(c,"FORCE STOP → VERIFY",dp(30),h-dp(20),8,Color.rgb(127,129,113),true);action.setEmpty();}
            else{int n=selected().size();txt(c,n+" SELECTED",dp(30),h-dp(70),10,Color.rgb(164,166,145),true);action.set(w-dp(174),h-dp(91),w-dp(30),h-dp(33));box(c,action.left,action.top,action.right,action.bottom,21,connected&&n>0?Color.rgb(245,216,76):Color.rgb(69,68,53));txt(c,"彻底清场",action.left+dp(30),action.top+dp(36),14,connected&&n>0?Color.rgb(22,22,16):Color.rgb(135,134,117),true);txt(c,"点击条目选择 / 取消",dp(30),h-dp(38),9,Color.rgb(118,120,105),false);}
        }

        void bananas(Canvas c,float w,float h){float[][] q={{.10f,.19f,-20,.85f},{.76f,.23f,24,.70f},{.18f,.52f,18,.65f},{.82f,.58f,-30,.90f},{.06f,.83f,30,.75f},{.69f,.88f,10,.60f}};for(float[] a:q)banana(c,w*a[0],h*a[1],a[2],a[3]);}
        void banana(Canvas c,float x,float y,float rot,float scale){c.save();c.translate(x,y);c.rotate(rot);c.scale(scale,scale);Path path=new Path();path.moveTo(dp(2),dp(16));path.cubicTo(dp(35),dp(48),dp(89),dp(45),dp(116),dp(8));path.cubicTo(dp(92),dp(64),dp(32),dp(74),dp(-4),dp(29));path.close();p.setStyle(Paint.Style.FILL);p.setColor(Color.argb(20,245,216,76));c.drawPath(path,p);stroke.setStyle(Paint.Style.STROKE);stroke.setStrokeWidth(dp(1.1f));stroke.setColor(Color.argb(30,255,235,122));c.drawPath(path,stroke);c.restore();}
        void row(Canvas c,AppRow a,float l,float t,float r,float b){if(b<0||t>getHeight())return;box(c,l,t,r,b,24,a.selected&&!a.protectedApp?Color.rgb(39,40,29):Color.rgb(25,27,21));if(a.selected&&!a.protectedApp){stroke.setStyle(Paint.Style.STROKE);stroke.setStrokeWidth(dp(1));stroke.setColor(Color.argb(120,245,216,76));c.drawRoundRect(l,t,r,b,dp(24),dp(24),stroke);}float il=l+dp(13),it=t+dp(13),is=dp(52);if(a.icon!=null){try{a.icon.setBounds((int)il,(int)it,(int)(il+is),(int)(it+is));a.icon.draw(c);}catch(Throwable ignored){}}else box(c,il,it,il+is,it+is,15,Color.rgb(54,56,43));txt(c,cut(a.label,18),l+dp(78),t+dp(29),13,Color.rgb(242,239,219),true);txt(c,cut(a.pkg,29),l+dp(78),t+dp(50),8.5f,Color.rgb(132,134,116),false);txt(c,a.processes+(a.processes==1?" process":" processes"),l+dp(78),t+dp(68),8,Color.rgb(176,179,151),true);if(a.protectedApp){box(c,r-dp(86),t+dp(25),r-dp(12),t+dp(53),14,Color.rgb(49,58,45));txt(c,a.reason,r-dp(78),t+dp(44),8,Color.rgb(180,222,177),true);}else{float cx=r-dp(29),cy=t+dp(39);p.setColor(a.selected?Color.rgb(245,216,76):Color.rgb(69,71,57));p.setStyle(Paint.Style.FILL);c.drawCircle(cx,cy,dp(12),p);if(a.selected){stroke.setStyle(Paint.Style.STROKE);stroke.setStrokeCap(Paint.Cap.ROUND);stroke.setStrokeWidth(dp(2));stroke.setColor(Color.rgb(28,28,20));Path ck=new Path();ck.moveTo(cx-dp(5),cy);ck.lineTo(cx-dp(1),cy+dp(4));ck.lineTo(cx+dp(6),cy-dp(5));c.drawPath(ck,stroke);}}}

        @Override public boolean onTouchEvent(MotionEvent e){if(cleaning)return true;float x=e.getX(),y=e.getY();switch(e.getActionMasked()){case MotionEvent.ACTION_DOWN:downY=lastY=y;dragged=false;return true;case MotionEvent.ACTION_MOVE:float dy=lastY-y;if(Math.abs(y-downY)>dp(5))dragged=true;if(dragged){float max=Math.max(0,apps.size()*dp(88)-Math.max(1,getHeight()-dp(116)-listTop()));scroll=Math.max(0,Math.min(max,scroll+dy));invalidate();}lastY=y;return true;case MotionEvent.ACTION_UP:if(dragged)return true;if(!shizuku.isEmpty()&&shizuku.contains(x,y)){openShizuku();return true;}if(action.contains(x,y)){List<AppRow>s=selected();if(connected&&!s.isEmpty())clean(s);return true;}float lt=listTop();if(y>=lt&&y<=getHeight()-dp(116)){int i=(int)((y-lt+scroll)/dp(88));if(i>=0&&i<apps.size()&&!apps.get(i).protectedApp){apps.get(i).selected=!apps.get(i).selected;invalidate();}}return true;}return super.onTouchEvent(e);}
    }
}
