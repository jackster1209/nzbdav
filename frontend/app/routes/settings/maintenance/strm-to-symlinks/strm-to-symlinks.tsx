import { Button } from "~/components/ui/button";
import { Alert } from "~/components/ui/feedback";
import { Icon } from "~/components/ui/icon";
import { useCallback, useEffect, useState } from "react";
import { receiveMessage } from "~/utils/websocket-util";

const cleanupTaskTopic = { 'st2sy': 'state' };

type ConvertStrmToSymlinksProps = {
    savedConfig: Record<string, string>
};

export function ConvertStrmToSymlinks({ savedConfig }: ConvertStrmToSymlinksProps) {
    // stateful variables
    const [connected, setConnected] = useState<boolean>(false);
    const [progress, setProgress] = useState<string | null>(null);
    const [isFetching, setIsFetching] = useState<boolean>(false);

    // derived variables
    const libraryDir = savedConfig["media.library-dir"];
    const isDone = progress?.startsWith("Done");
    const isFinished = progress?.startsWith("Done") || progress?.startsWith("Failed");
    const isRunning = !isFinished && (isFetching || progress !== null);
    const isRunButtonEnabled = !!libraryDir && connected && !isRunning;
    const runButtonVariant = isRunButtonEnabled ? 'success' : 'secondary';
    const runButtonLabel = isRunning ? "Running..." : "Run Task";

    // effects
    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        function connect() {
            ws = new WebSocket(window.location.origin.replace(/^http/, 'ws'));
            ws.onmessage = receiveMessage((_, message) => setProgress(message));
            ws.onopen = () => { setConnected(true); ws.send(JSON.stringify(cleanupTaskTopic)); }
            ws.onclose = () => { !disposed && setTimeout(() => connect(), 1000); setProgress(null) };
            ws.onerror = () => { ws.close() };
            return () => { disposed = true; ws.close(); }
        }
        return connect();
    }, [setProgress, setConnected]);

    // events
    const onRun = useCallback(async () => {
        setIsFetching(true);
        await fetch("/api/convert-strm-to-symlinks");
        setIsFetching(false);
    }, [setIsFetching]);

    return (
        <>
            {!libraryDir &&
                <Alert className={'mb-3'} variant="warning">
                    Warning
                    <ul className={'mt-2 list-disc space-y-1 pl-5'}>
                        <li className={'text-xs'}>
                            You must first configure the Library Directory setting before running this task.
                            Head over to the Repairs tab.
                        </li>
                    </ul>
                </Alert>
            }
            {libraryDir &&
                <Alert className={'mb-3'} variant="danger">
                    <span className="font-semibold">Danger</span>
                    <ul className={'mt-2 list-disc space-y-1 pl-5'}>
                        <li className={'text-xs'}>
                            Make a backup of your entire Library Dir prior to running this task
                        </li>
                        <li className={'text-xs'}>
                            Strm files will be deleted from `{libraryDir}` and will not be recoverable without a backup.
                        </li>
                    </ul>
                </Alert>
            }
            <div className={'space-y-3'}>
                <div className="space-y-2">
                    <div className={'flex flex-col gap-3 sm:flex-row sm:items-center'}>
                        <Button
                            className={'shrink-0'}
                            variant={runButtonVariant}
                            onClick={onRun}
                            disabled={!isRunButtonEnabled}
                        >
                            <Icon name={isRunning ? "progress_activity" : "play_arrow"} className={`!text-[18px] ${isRunning ? "animate-spin" : ""}`} />
                            {runButtonLabel}
                        </Button>
                        <div className={'font-mono text-xs text-slate-300'}>
                            {progress}
                        </div>
                    </div>
                    <p className="text-[11px] leading-relaxed text-base-content/45" id="cleanup-task-progress-help">
                        <br />
                        This task will scan your organized media library for all *.strm files.
                        Every *.strm file that links to nzbdav media will be deleted and be replaced by a symlink.
                        The newly created symlinks will all point to the corresponding file within your rclone mount.
                    </p>
                </div>
            </div>
        </>
    );
}