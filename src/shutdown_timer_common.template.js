// ES3-compatible shared logic for MSHTML IE9 and Windows Script Host.
function parseState(text) {
    var job = {}, lines = String(text || "").split(/\r?\n/);
    for (var i = 0; i < lines.length; i++) {
        var pos = lines[i].indexOf("=");
        if (pos > 0) { job[lines[i].substring(0, pos)] = lines[i].substring(pos + 1); }
    }
    if (job.version !== "1" || !/^[0-9-]+$/.test(job.token || "") ||
            !/^(scheduled|cancelled|expired)$/.test(job.status || "")) { return null; }
    var fields = ["started", "deadline", "heartbeat", "warn5", "warnFinal"];
    for (var j = 0; j < fields.length; j++) {
        var key = fields[j];
        if (!/^\d+$/.test(String(job[key] || "0"))) { return null; }
        job[key] = Number(job[key] || 0);
        if (!isFinite(job[key])) { return null; }
    }
    if (job.deadline <= job.started || job.deadline - job.started > 315360000000) { return null; }
    return job;
}

function serializeState(job) {
    var keys = ["version", "token", "status", "started", "deadline", "boot", "heartbeat", "warn5", "warnFinal", "reason"];
    var lines = [];
    for (var i = 0; i < keys.length; i++) {
        lines.push(keys[i] + "=" + String(job[keys[i]] === undefined ? "" : job[keys[i]]).replace(/[\r\n=]/g, ""));
    }
    return lines.join("\r\n") + "\r\n";
}

function remainingSeconds(job, now) { return Math.max(0, Math.ceil((job.deadline - now) / 1000)); }
function pad2(n) { return n < 10 ? "0" + n : String(n); }
function formatCountdown(seconds) {
    return pad2(Math.floor(seconds / 3600)) + ":" + pad2(Math.floor(seconds / 60) % 60) + ":" + pad2(seconds % 60);
}
function reminderKey(job, now) {
    if (job.status !== "scheduled" || job.deadline <= now) { return ""; }
    var remaining = remainingSeconds(job, now);
    var duration = Math.round((job.deadline - job.started) / 1000);
    // Short plans get one reminder halfway through, up to 60 seconds before shutdown.
    var finalThreshold = Math.min(60, Math.floor(duration / 2));
    if (remaining <= finalThreshold && !job.warnFinal) { return "warnFinal"; }
    if (duration > 300 && remaining <= 300 && !job.warn5 && !job.warnFinal) { return "warn5"; }
    return "";
}

function reminderMessage(job, now) {
    var seconds = remainingSeconds(job, now);
    var time = Math.floor(seconds / 60) + " 分 " + (seconds % 60) + " 秒";
    return "距离自动关机还有 " + time + "。\n\n如果正在使用电脑，可以立即取消关机。\n现在取消自动关机吗？\n\n点击“是”取消关机；点击“否”继续倒计时。\n20 秒内未选择，将保留原计划。";
}

function createStorage(fso, filePath) {
    var folder = fso.GetParentFolderName(filePath), lockPath = filePath + ".lock";
    if (!fso.FolderExists(folder)) { fso.CreateFolder(folder); }
    return {
        path: filePath,
        read: function () {
            var file = null;
            try {
                if (!fso.FileExists(filePath)) { return null; }
                file = fso.OpenTextFile(filePath, 1, false, 0);
                return parseState(file.ReadAll());
            } catch (error) { return null; }
            finally { if (file) { try { file.Close(); } catch (ignored) {} } }
        },
        write: function (job) {
            var file = fso.CreateTextFile(filePath, true, false);
            try { file.Write(serializeState(job)); } finally { file.Close(); }
        },
        acquire: function () {
            try {
                if (fso.FolderExists(lockPath)) {
                    var age = new Date().getTime() - new Date(fso.GetFolder(lockPath).DateCreated).getTime();
                    if (age > 30000) { fso.DeleteFolder(lockPath, true); }
                }
                fso.CreateFolder(lockPath);
                return true;
            } catch (error) { return false; }
        },
        release: function () { try { fso.DeleteFolder(lockPath, true); } catch (ignored) {} }
    };
}

function getBootStamp() {
    // The native helper records and validates the boot marker without WMI.
    return "";
}
