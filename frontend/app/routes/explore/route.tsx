import type { Route } from "./+types/route";
import { Breadcrumbs } from "./breadcrumbs/breadcrumbs";
import { Link, redirect, useLocation, useNavigation } from "react-router";
import {
    backendClient,
    WebdavDirectoryNotFoundError,
    type DirectoryItem,
} from "~/clients/backend-client.server";
import { useCallback } from "react";
import { lookup as getMimeType } from 'mime-types';
import { getDownloadKey } from "~/auth/downloads.server";
import { Loading } from "../_index/components/loading/loading";
import { formatFileSize } from "~/utils/file-size";
import { ItemMenu } from "./item-menu/item-menu";
import { Icon } from "~/components/ui";

export type ExplorePageData = {
    parentDirectories: string[],
    items: (DirectoryItem | ExploreFile)[],
    error: "not-found" | null,
}

export type ExploreFile = DirectoryItem & {
    mimeType: string,
    downloadKey: string,
}


export async function loader({ request, params }: Route.LoaderArgs) {
    // if path ends in trailing slash, remove it
    if (request.url.endsWith('/')) return redirect(request.url.slice(0, -1));

    // Single-fetch navigation requests use an internal `.data` URL, so derive
    // the WebDAV path from the matched wildcard rather than request.url.
    const path = decodeURIComponent(params["*"] ?? "");
    try {
        return {
            parentDirectories: getParentDirectories(path),
            error: null,
            items: (await backendClient.listWebdavDirectory(path)).map(x => {
                if (x.isDirectory) return x;
                return {
                    ...x,
                    mimeType: getMimeType(x.name),
                    downloadKey: getDownloadKey(getRelativePath(path, x.name))
                };
            })
        };
    } catch (error) {
        if (!(error instanceof WebdavDirectoryNotFoundError)) throw error;
        return {
            parentDirectories: getParentDirectories(path),
            items: [],
            error: "not-found" as const,
        };
    }
}

export default function Explore({ loaderData }: Route.ComponentProps) {
    return (
        <Body {...loaderData} />
    );
}

function Body(props: ExplorePageData) {
    const location = useLocation();
    const navigation = useNavigation();
    const isNavigating = Boolean(navigation.location);

    const items = props.items;
    const parentDirectories = isNavigating
        ? getParentDirectories(getWebdavPathDecoded(navigation.location!.pathname))
        : props.parentDirectories;

    const getDirectoryPath = useCallback((directoryName: string) => {
        return `${location.pathname}/${encodeURIComponent(directoryName)}`;
    }, [location.pathname]);

    const getFilePath = useCallback((file: ExploreFile) => {
        const pathname = getWebdavPath(location.pathname);
        const relativePath = getRelativePath(pathname, encodeURIComponent(file.name));
        const extension = getExtension(file.name);
        const extensionQueryParam = extension ? `&extension=${extension}` : '';
        return `/view/${relativePath}?downloadKey=${file.downloadKey}${extensionQueryParam}`;
    }, [location.pathname]);

    return (
        <div className="absolute flex min-h-full min-w-full flex-col px-4 py-4 text-base text-slate-300 md:px-8">
            <Breadcrumbs parentDirectories={parentDirectories} />
            {!isNavigating && props.error === "not-found" && (
                <div className="surface-card flex min-h-[320px] flex-col items-center justify-center gap-3 border border-slate-700/70 px-6 text-center">
                    <Icon name="folder_off" className="!text-[48px] text-amber-400" />
                    <h2 className="text-xl font-semibold text-white">Directory unavailable</h2>
                    <p className="max-w-md text-sm leading-relaxed text-slate-400">
                        {parentDirectories.length === 0
                            ? "The WebDAV root is not available yet. It may still be initializing."
                            : "This WebDAV directory does not exist or may have moved."}
                    </p>
                    <div className="mt-2 flex flex-wrap justify-center gap-2">
                        {parentDirectories.length > 0 && (
                            <Link to="/explore" className="button-base button-small border border-slate-50/20 bg-white/5 text-slate-200 hover:bg-white/10">
                                <Icon name="home" className="!text-[18px]" />
                                WebDAV root
                            </Link>
                        )}
                        <Link reloadDocument to={location.pathname} className="button-base button-small bg-blue-600 text-white hover:bg-blue-700">
                            <Icon name="refresh" className="!text-[18px]" />
                            Try again
                        </Link>
                    </div>
                </div>
            )}
            {!isNavigating && items.length > 0 &&
                <div className="overflow-visible rounded-lg border border-slate-700/70 bg-gray-800 shadow-md">
                    {items.filter(x => x.isDirectory).map((x, index) =>
                        <div key={`${index}_dir_item`} className={getClassName(x)}>
                            <Link
                                to={getDirectoryPath(x.name)}
                                className="flex min-w-0 flex-1 items-center gap-3 p-3 text-inherit no-underline transition-colors hover:bg-white/5 active:bg-white/10 md:p-4"
                            >
                                <Icon name="folder" filled className="shrink-0 !text-[40px] text-slate-400" />
                                <div className="break-all">{x.name}</div>
                            </Link>
                        </div>
                    )}
                    {items.filter(x => !x.isDirectory).map((x, index) =>
                        <div key={`${index}_file_item`} className={getClassName(x)}>
                            <a
                                href={getFilePath(x as ExploreFile)}
                                className="flex min-w-0 flex-1 items-center gap-3 py-3 pl-3 pr-1 text-inherit no-underline transition-colors hover:bg-white/5 active:bg-white/10 md:py-4 md:pl-4"
                            >
                                <Icon name={getIcon(x as ExploreFile)} className="shrink-0 !text-[40px] text-slate-400" />
                                <div className="flex min-w-0 flex-col gap-1 leading-none">
                                    <div className="break-all">{x.name}</div>
                                    <div className="font-mono text-xs text-slate-500">{formatFileSize(x.size)}</div>
                                </div>
                            </a>
                            <ItemMenu
                                exploreFile={x as ExploreFile}
                                previewPath={getFilePath(x as ExploreFile)} />
                        </div>
                    )}
                </div>
            }
            {!isNavigating && props.error === null && items.length === 0 && (
                <div className="surface-card flex min-h-[320px] flex-col items-center justify-center gap-3 border border-slate-700/70 px-6 text-center">
                    <Icon name="folder_open" className="!text-[48px] text-slate-500" />
                    <h2 className="text-xl font-semibold text-white">This directory is empty</h2>
                    <p className="text-sm text-slate-400">There are no files or folders to display.</p>
                </div>
            )}
            {isNavigating && <Loading className="min-h-0 flex-1" />}
        </div >
    );
}

function getExtension(filename: string): string | undefined {
    const lastDotIndex = filename.lastIndexOf('.');
    if (lastDotIndex === -1 || lastDotIndex === 0) return undefined;
    return filename.slice(lastDotIndex);
}

function getIcon(file: ExploreFile) {
    if (file.name.toLowerCase().endsWith(".mkv")) return "movie";
    if (file.mimeType === "application/mp4") return "movie";
    if (file.mimeType && file.mimeType.startsWith("video")) return "movie";
    if (file.mimeType && file.mimeType.startsWith("image")) return "image";
    return "draft";
}

function getWebdavPath(pathname: string): string {
    if (pathname.startsWith("/")) pathname = pathname.slice(1);
    if (pathname.startsWith("explore")) pathname = pathname.slice(7);
    if (pathname.startsWith("/")) pathname = pathname.slice(1);
    return pathname;
}

function getWebdavPathDecoded(pathname: string): string {
    return decodeURIComponent(getWebdavPath(pathname));
}

function getRelativePath(path: string, filename: string) {
    if (path === "") return filename;
    return `${path}/${filename}`;
}

function getParentDirectories(webdavPath: string): string[] {
    return webdavPath == "" ? [] : webdavPath.split('/');
}

function getClassName(item: DirectoryItem | ExploreFile) {
    const hidden = item.name.startsWith('.') ? " opacity-50" : "";
    return `relative flex border-b border-slate-700/70 last:border-b-0${hidden}`;
}