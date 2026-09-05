package com.arashi.lunwu;

import android.os.RemoteException;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;

public class ShizukuShellService extends IRemoteShell.Stub {
    public ShizukuShellService() {}

    @Override
    public String exec(String command) throws RemoteException {
        StringBuilder out = new StringBuilder();
        try {
            Process p = new ProcessBuilder("sh", "-c", command).redirectErrorStream(true).start();
            try (BufferedReader br = new BufferedReader(new InputStreamReader(p.getInputStream(), StandardCharsets.UTF_8))) {
                String line;
                while ((line = br.readLine()) != null) out.append(line).append('\n');
            }
            out.append("\n__LUNWU_EXIT__=").append(p.waitFor());
        } catch (Throwable t) {
            out.append("__LUNWU_ERROR__=").append(t.getClass().getSimpleName()).append(": ").append(t.getMessage());
        }
        return out.toString();
    }

    @Override
    public void destroy() {
        System.exit(0);
    }
}
