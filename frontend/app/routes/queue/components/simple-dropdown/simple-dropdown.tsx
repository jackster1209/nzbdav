import { memo, useCallback, useState, type ChangeEvent, type RefObject } from "react"

export type SimpleDropdownProps = {
    type?: "plain" | "bordered"
    options: string[],
    value?: string,
    onChange?: (value: string) => void,
    valueRef?: RefObject<string>,
}

export const SimpleDropdown = memo(({ type, options, value, onChange, valueRef }: SimpleDropdownProps) => {
    if (!valueRef && (!value || !onChange)) {
        throw new Error("SimpleDropdown requires either the valueRef prop or both the value and onChange props.")
    }

    const [internalValue, setInternalValue] = useState(options.length > 0 ? options[0] : "");
    const renderedValue = value ?? internalValue;

    const handleNativeChange = useCallback((e: ChangeEvent<HTMLSelectElement>) => {
        const next = e.target.value;
        if (valueRef) {
            setInternalValue(next);
            valueRef.current = next;
        }
        onChange?.(next);
    }, [valueRef, onChange]);

    return (
        <select
            aria-label="Select option"
            className={`select select-xs ${type === "bordered" ? "select-bordered" : "select-ghost"} w-auto min-w-20`}
            value={renderedValue}
            onChange={handleNativeChange}
        >
            {options.map(option => (
                <option key={option} value={option}>{option}</option>
            ))}
        </select>
    );
});
