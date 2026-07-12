import { Form, Link, useLocation, useNavigation } from "react-router";
import type React from "react";
import { LiveUsenetConnections } from "../live-usenet-connections/live-usenet-connections";
import { LiveReads } from "../live-reads/live-reads";
import { Icon } from "~/components/ui";

export type LeftNavigationProps = {
    version?: string,
    updateAvailable?: { latestVersion: string; releaseUrl: string } | null,
    isFrontendAuthDisabled?: boolean,
    hasUsenetProviders?: boolean,
    isWatchdogEnabled?: boolean,
}

export function LeftNavigation({
    version,
    updateAvailable,
    isFrontendAuthDisabled,
    hasUsenetProviders,
    isWatchdogEnabled,
}: LeftNavigationProps) {
    return (
        <div className="flex h-full flex-col px-3 py-5 text-slate-400">
            <nav className="flex flex-col gap-1">
                <Item target="/overview" icon="dashboard">Overview</Item>
                <Item target="/queue" icon="list_alt">Queue</Item>
                {isWatchdogEnabled && <Item target="/watchdog" icon="monitor_heart">Watchdog</Item>}
                <Item target="/watchtower" icon="cell_tower">Watchtower</Item>
                <Item target="/explore" icon="folder_open">Files</Item>
                <Item target="/health" icon="health_and_safety">Health</Item>
                <Item target="/logs" icon="description">Logs</Item>
                <Item target="/search" icon="search">Search</Item>
                <Item target="/settings" icon="settings">Settings</Item>
            </nav>
            <LiveUsenetConnections hasUsenetProviders={!!hasUsenetProviders} />
            <LiveReads />

            <footer className="mt-auto border-t border-slate-800 pt-4 text-xs text-slate-500">
                <div className="mb-3 flex items-center gap-3">
                    <Link
                        to="https://github.com/nzbdav/nzbdav"
                        className="flex items-center gap-1 hover:text-blue-400"
                    >
                        <Icon name="code" className="!text-[16px]" />
                        GitHub
                    </Link>
                    <Link
                        to="https://github.com/nzbdav/nzbdav/releases"
                        className="hover:text-blue-400"
                    >
                        Changelog
                    </Link>
                </div>
                <VersionStatusCard version={version} updateAvailable={updateAvailable} />
                {!isFrontendAuthDisabled && (
                    <Form method="post" action="/logout">
                        <input name="confirm" value="true" type="hidden" />
                        <button
                            className="mt-3 flex w-full items-center gap-2 rounded-md px-2 py-2 text-left text-sm text-slate-400 hover:bg-white/10 hover:text-white"
                            type="submit"
                        >
                            <Icon name="logout" className="!text-[18px]" />
                            Logout
                        </button>
                    </Form>
                )}
            </footer>
        </div>
    );
}

function VersionStatusCard({
    version,
    updateAvailable,
}: {
    version?: string,
    updateAvailable?: { latestVersion: string; releaseUrl: string } | null,
}) {
    const displayVersion = version || "unknown";
    const hasUpdate = Boolean(updateAvailable);

    const content = (
        <>
            <Icon name="hard_drive" className="!text-[22px] shrink-0" />
            <div className="min-w-0 flex-1 truncate">
                <div className="font-bold text-white">NzbDav Stable</div>
                <div className="truncate font-mono text-[11px] tracking-wide text-slate-400">
                    {updateAvailable
                        ? `${displayVersion} → v${updateAvailable.latestVersion}`
                        : displayVersion}
                </div>
            </div>
            {updateAvailable && (
                <Icon name="arrow_circle_up" className="!text-[22px] shrink-0 text-blue-400" />
            )}
        </>
    );

    const className = `flex w-full items-center gap-2 rounded-lg border p-2 text-xs transition-all duration-200 ${
        hasUpdate
            ? "border-blue-500/50 bg-blue-500/15 text-slate-200 hover:border-blue-400/70 hover:bg-blue-500/25"
            : "border-slate-700/70 bg-slate-900 text-slate-300"
    }`;

    if (updateAvailable) {
        return (
            <Link
                to={updateAvailable.releaseUrl}
                className={className}
                target="_blank"
                rel="noreferrer"
            >
                {content}
            </Link>
        );
    }

    return <div className={className}>{content}</div>;
}

function Item({ target, icon, children }: { target: string, icon: string, children: React.ReactNode }) {
    const location = useLocation();
    const navigation = useNavigation();
    const pathname = navigation.location?.pathname ?? location.pathname;
    const isSelected = pathname.startsWith(target);
    return (
        <Link
            to={target}
            className={`group flex items-center gap-3 rounded-md px-3 py-2.5 text-sm font-medium transition-all ${
                isSelected
                    ? "bg-blue-500/15 text-blue-400"
                    : "text-slate-400 hover:bg-white/10 hover:text-white"
            }`}
        >
            <Icon name={icon} filled={isSelected} className="!text-[21px] transition-transform group-hover:scale-105" />
            {children}
        </Link>
    );
}