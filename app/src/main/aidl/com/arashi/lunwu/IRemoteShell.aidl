package com.arashi.lunwu;

interface IRemoteShell {
    String exec(String command);
    void destroy();
}
