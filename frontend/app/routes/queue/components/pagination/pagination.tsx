import { SimpleDropdown } from "../simple-dropdown/simple-dropdown";
import { memo } from "react";
import { Icon } from "~/components/ui";

export type PaginationProps = {
    pageNumber: number,
    totalPages: number,
    onPageSelected?: (page: number) => void,
}

export const Pagination = memo(({ pageNumber, totalPages, onPageSelected }: PaginationProps) => {
    const handlePageClick = (page: number) => {
        if (onPageSelected && page !== pageNumber && page >= 1 && page <= totalPages) {
            onPageSelected(page);
        }
    };

    const handleDropdownChange = (value: string) => {
        const page = parseInt(value, 10);
        if (onPageSelected && !isNaN(page)) {
            onPageSelected(page);
        }
    };

    const pageOptions = Array.from({ length: totalPages }, (_, i) => String(i + 1));

    return (
        <div className="join join-horizontal flex flex-wrap items-center justify-center">
            <button
                type="button"
                className="btn btn-ghost btn-xs join-item"
                disabled={pageNumber <= 1}
                onClick={() => handlePageClick(pageNumber - 1)}
            >
                <Icon name="chevron_left" className="!text-[16px]" /> Prev
            </button>

            <div className="join-item flex items-center gap-2 border-x border-base-content/10 px-3 text-xs font-medium text-base-content/60">
                <span>Page</span>
                <SimpleDropdown
                    type="bordered"
                    options={pageOptions}
                    value={String(pageNumber)}
                    onChange={handleDropdownChange}
                />
                <span>of {totalPages}</span>
            </div>

            <button
                type="button"
                className="btn btn-ghost btn-xs join-item"
                disabled={pageNumber >= totalPages}
                onClick={() => handlePageClick(pageNumber + 1)}
            >
                Next <Icon name="chevron_right" className="!text-[16px]" />
            </button>
        </div>
    );
});
