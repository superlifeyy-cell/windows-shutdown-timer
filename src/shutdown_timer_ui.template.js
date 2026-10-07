var shell = null, fso = null, storage = null;
var busy = false, resizeTimer = null, fitting = false, hideTimer = null;
var cachedJob = null, bootStamp = "", lastHelperAttempt = 0, helperError = "";

function getShell() { if (!shell) shell = new ActiveXObject("WScript.Shell"); return shell; }
function getFSO() { if (!fso) fso = new ActiveXObject("Scripting.FileSystemObject"); return fso; }
function getStorage() {
    if (!storage) storage = createStorage(getFSO(), getShell().ExpandEnvironmentStrings("%APPDATA%") + "\\ShutdownTimer\\state.ini");
    return storage;
}
function getAppDirectory() {
    var path = decodeURIComponent(window.location.pathname).replace(/^\/(\w:)/,"$1").replace(/\//g,"\\");
    if (window.location.hostname) path = "\\\\" + window.location.hostname + path;
    return getFSO().GetParentFolderName(path);
}
function helperPath() { return getAppDirectory() + "\\shutdown_timer_tray.exe"; }
function clockNow() { return new Date().getTime(); }
function showError(message) { alert(message); }
function cancelPendingHide() { if(hideTimer !== null) { window.clearTimeout(hideTimer); hideTimer=null; } }
function hideAfterConfirmation(job) {
    cancelPendingHide();
    var token=job.token;
    hideTimer=window.setTimeout(function() {
        hideTimer=null;
        var current=getStorage().read();
        if(current && current.token === token && current.status === "scheduled" && current.deadline > clockNow()) {
            try { startHelper("--hide"); } catch(error) { showError("无法隐藏窗口，请手动最小化。\n"+(error.description || error.message || "")); }
        }
    },2000);
}
function startHelper(mode) {
    if (!getFSO().FileExists(helperPath())) throw new Error("找不到后台提醒程序，请重新双击定时关机 EXE 修复程序文件。");
    getShell().Run('"' + helperPath() + '" ' + mode, 0, false);
    lastHelperAttempt = clockNow(); helperError = "";
}
function setText(id, text) { document.getElementById(id).innerText = text; }
function displayTime(stamp) {
    var date = new Date(stamp);
    return date.getFullYear() + "-" + pad2(date.getMonth()+1) + "-" + pad2(date.getDate()) + " " + pad2(date.getHours()) + ":" + pad2(date.getMinutes()) + ":" + pad2(date.getSeconds());
}
function renderStatus(job) {
    var panel = document.getElementById("statusPanel"), cancel = document.getElementById("cancelButton");
    var now = clockNow(), active = job && job.status === "scheduled" && job.deadline > now;
    var showDetails = job && job.status !== "cancelled";
    document.getElementById("statusLines").style.display = showDetails ? "inline-block" : "none";
    document.getElementById("shutdownAt").style.display = showDetails ? "block" : "none";
    var note="";
    panel.className = "status"; cancel.className = "btn cancel";
    if (active) {
        var left = remainingSeconds(job, now);
        panel.className += left <= 60 ? " urgent-status" : left <= 300 ? " warning-status" : " active-status";
        if (left <= 60) cancel.className += " urgent-cancel";
        setText("statusLabel", "自动关机已启动");
        setText("countdown", formatCountdown(left));
        setText("shutdownAt", "预计关机时间：" + displayTime(job.deadline));
        var background = job.heartbeat && now - job.heartbeat < 10000;
        note = left <= 300 ? "即将关机，若仍在使用电脑可随时取消" :
            helperError && !background ? "后台提醒未运行，请保持主窗口开启。" : "";
    } else {
        var label = !job ? "暂无关机计划" : job.status === "cancelled" ? (job.reason === "newboot" ? "上次计划已结束" : "自动关机已取消") : "已到计划关机时间";
        setText("statusLabel", label); setText("countdown", job && job.status !== "cancelled" ? "00:00:00" : "--:--:--");
        setText("shutdownAt", showDetails ? "上次计划关机时间：" + displayTime(job.deadline) : "");
    }
    setText("statusNote",note);
    document.getElementById("statusNote").style.display = note ? "block" : "none";
    fitLayout();
}
function refreshStatus() {
    try {
        var job = getStorage().read();
        if (job) {
            if (job.status === "scheduled" && bootStamp && job.boot && bootStamp !== job.boot) {
                job.status = "cancelled"; job.reason = "newboot";
            }
            cachedJob = job;
        }
        renderStatus(cachedJob);
        if (cachedJob && cachedJob.status === "scheduled" && cachedJob.deadline > clockNow() &&
                clockNow() - cachedJob.heartbeat > 15000 && clockNow() - lastHelperAttempt > 60000) {
            try { startHelper("--ensure"); } catch (error) { helperError = error.message || error.description; }
        }
    } catch (error) { helperError = error.message || error.description; }
}

function setShutdown(seconds, message) {
    if (busy) return;
    if (!isFinite(seconds) || seconds < 60 || seconds > 315360000 || Math.floor(seconds) !== seconds) {
        showError("请输入有效的关机时间。"); return;
    }
    busy = true;
    cancelPendingHide();
    var store = null, locked = false, scheduled = false, saved = false;
    try {
        if (!getFSO().FileExists(helperPath())) throw new Error("找不到后台提醒程序，请把压缩包中的程序文件放在同一文件夹。");
        store = getStorage(); locked = store.acquire();
        if (!locked) { showError("正在处理关机计划，请稍后再试。"); return; }
        var previous = store.read();
        var oldResult = getShell().Run("shutdown.exe -a", 0, true);
        if (oldResult !== 0 && oldResult !== 1116) {
            showError("无法取消原来的关机计划，请重试。\n错误代码：" + oldResult); return;
        }
        var started = clockNow();
        var result = getShell().Run("shutdown.exe -s -t " + seconds, 0, true);
        if (result !== 0) {
            if (previous) { previous.status="cancelled"; previous.reason="replace-failed"; previous.heartbeat=0; store.write(previous); cachedJob=previous; }
            showError("新计划未设置成功，原计划已取消，请重新设置。\n错误代码：" + result); return;
        }
        scheduled = true;
        var job = { version:"1",token:started+"-"+Math.floor(Math.random()*1000000000),status:"scheduled",
            started:started,deadline:started+seconds*1000,boot:bootStamp,heartbeat:0,warn5:0,warnFinal:0,reason:"" };
        store.write(job); saved = true; cachedJob = job;
    } catch (error) {
        if (scheduled && !saved) {
            var rollback = getShell().Run("shutdown.exe -a",0,true);
            showError(rollback === 0 || rollback === 1116 ? "无法保存关机计划，已撤销本次关机。\n"+(error.description || error.message || "") : "无法保存计划且取消失败，请立即点击取消关机。\n错误代码："+rollback);
        } else showError("无法设置定时关机。\n" + (error.description || error.message || ""));
        return;
    } finally { if (locked) store.release(); busy = false; renderStatus(cachedJob); }
    try { startHelper("--ensure"); hideAfterConfirmation(cachedJob); }
    catch (error) { helperError=error.message || error.description; renderStatus(cachedJob); showError("关机计划已设置，但后台提醒未启动，请保持主窗口开启或取消计划。\n"+helperError); }
}
function setCustomShutdown() {
    var input = document.getElementById("customMinutes"), text = input.value.replace(/^\s+|\s+$/g, "");
    if (!/^\d+$/.test(text) || Number(text) < 1) { showError("请输入大于 0 的整数分钟数，例如 45。"); input.focus(); input.select(); return; }
    var minutes = Number(text);
    if (minutes > 5256000) { showError("时间过长，请输入不超过 5256000 的分钟数。"); input.focus(); input.select(); return; }
    setShutdown(minutes * 60);
}
function customKeyDown(event) {
    event = event || window.event;
    if (event.keyCode === 13) { if (event.preventDefault) event.preventDefault(); event.returnValue=false; setCustomShutdown(); return false; }
    return true;
}
function cancelShutdown() {
    if (busy) return;
    cancelPendingHide();
    busy = true;
    var store=null,locked=false;
    try {
        store=getStorage(); locked=store.acquire();
        if (!locked) { showError("正在处理关机计划，请稍后再试。"); return; }
        var result=getShell().Run("shutdown.exe -a",0,true);
        if (result !== 0 && result !== 1116) { showError("取消关机未成功，请重试。\n错误代码："+result); return; }
        var now=clockNow(),job=store.read() || {version:"1",token:now+"-0",started:now,deadline:now+60000,boot:bootStamp,warn5:0,warnFinal:0};
        job.status="cancelled"; job.reason="manual"; job.heartbeat=0;
        store.write(job); cachedJob=job;
    } catch (error) { showError("无法取消定时关机。\n"+(error.description || error.message || "")); }
    finally { if(locked) store.release(); busy=false; renderStatus(cachedJob); }
}
function minimizeToTray() {
    cancelPendingHide();
    try { startHelper("--hide"); } catch(error) { showError("无法最小化到托盘。\n"+(error.description || error.message || "")); }
}
function fitLayout() {
    if (fitting || !document.getElementById("content")) return;
    fitting=true;
    try {
        var unit=20,viewport=document.documentElement,content=document.getElementById("content");
        if((viewport.clientHeight || document.body.clientHeight) < 1) return;
        document.body.style.fontSize=unit+"px";
        for(var i=0;i<10;i++) {
            var available=viewport.clientHeight || document.body.clientHeight,required=content.offsetHeight+4*unit+4;
            if(required<=available) break;
            unit=unit*available/required*.98; document.body.style.fontSize=unit+"px";
        }
    } finally { fitting=false; }
}
window.onload=function() {
    var w=Math.min(560,screen.availWidth-24),h=Math.min(800,screen.availHeight-24);
    window.resizeTo(w,h);
    var left=(typeof screen.availLeft === "number" ? screen.availLeft : 0)+Math.max(0,(screen.availWidth-w)/2);
    var top=(typeof screen.availTop === "number" ? screen.availTop : 0)+Math.max(0,(screen.availHeight-h)/2);
    window.moveTo(Math.round(left),Math.round(top));
    bootStamp=getBootStamp(); refreshStatus();
    try { startHelper("--ensure"); } catch(error) { helperError=error.description || error.message; }
    window.setInterval(refreshStatus,1000); fitLayout();
    window.setTimeout(function() {
        fitLayout();
        try { startHelper("--ready"); }
        catch(error) { showError("主界面已加载，但无法显示窗口。请确认后台程序文件完整。\n"+(error.description || error.message || "")); }
    },120);
};
window.onresize=function() { window.clearTimeout(resizeTimer); resizeTimer=window.setTimeout(fitLayout,60); };
window.onfocus=function() { cancelPendingHide(); };
window.onunload=function() {
    cancelPendingHide();
    try { startHelper("--exit"); }
    catch(error) { showError("无法联系后台程序。若仍有关机计划，请重新打开程序后取消。\n"+(error.description || error.message || "")); }
};
