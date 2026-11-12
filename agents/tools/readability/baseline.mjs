// The ratchet of measure.mjs: what each file is over the limits by, kept in baseline.json, so that no file gets worse
// while the old debt is paid off (agents/docs/development/readability-refactoring-plan.md). See README.md.

import { readFileSync, writeFileSync } from 'node:fs';

/** The measures a file can owe, in the order they are written and reported. */
const MEASURES = [
    'fileLines',
    'longLines',
    'types',
    'longFunctions',
    'deepFunctions',
    'manyParameterFunctions',
    'heavyClasses',
    'longTemplates',
];

/**
 * What each file owes, by measure: its line count when it is over the file limit, its lines over the line limit, its
 * top-level types when there are several, and how many of its functions, classes and templates are over a limit.
 * Files within every limit are left out.
 */
export function debtsOf(areas, limits) {
    const debts = new Map();
    const add = (file, measure, amount = 1) => {
        const debt = debts.get(file) ?? {};
        debt[measure] = (debt[measure] ?? 0) + amount;
        debts.set(file, debt);
    };
    for (const area of areas) {
        const fileLimit = area.tests ? limits.testFileLines : limits.fileLines;
        for (const file of area.files) {
            if (file.lines > fileLimit)
                add(file.file, 'fileLines', file.lines);
            if (file.longLines)
                add(file.file, 'longLines', file.longLines);
        }
        for (const fn of area.functions) {
            if (fn.lines > limits.functionLines)
                add(fn.file, 'longFunctions');
            if (fn.depth > limits.depth)
                add(fn.file, 'deepFunctions');
            if (fn.parameters > limits.parameters)
                add(fn.file, 'manyParameterFunctions');
        }
        for (const type of area.constructors.filter(type => type.count > limits.dependencies))
            add(type.file, 'heavyClasses');
        for (const template of area.templates.filter(template => template.lines > limits.templateLines))
            add(template.file, 'longTemplates');
        for (const file of area.manyTypes)
            add(file.file, 'types', file.count);
    }
    return debts;
}

/** The baseline file's debts, empty when there is no file yet. */
export function readBaseline(path) {
    let text;
    try {
        text = readFileSync(path, 'utf8');
    } catch (error) {
        if (error.code === 'ENOENT')
            return new Map();
        throw error;
    }
    return new Map(Object.entries(JSON.parse(text).files));
}

/** The measures where a file owes more than in the baseline (`worse`) or less (`better`). */
export function compare(debts, baseline) {
    const worse = [];
    const better = [];
    for (const file of new Set([...debts.keys(), ...baseline.keys()])) {
        for (const measure of MEASURES) {
            const now = debts.get(file)?.[measure] ?? 0;
            const before = baseline.get(file)?.[measure] ?? 0;
            if (now > before)
                worse.push({ file, measure, before, now });
            else if (now < before)
                better.push({ file, measure, before, now });
        }
    }
    return { worse, better };
}

/** Each measure summed over all files. */
export function totalsOf(debts) {
    const totals = Object.fromEntries(MEASURES.map(measure => [measure, 0]));
    for (const debt of debts.values()) {
        for (const measure of MEASURES)
            totals[measure] += debt[measure] ?? 0;
    }
    return totals;
}

/** Writes the debts sorted by file, one file per line, so that a change to the baseline reads as a short diff. */
export function writeBaseline(path, debts) {
    const files = [...debts.keys()].sort();
    const entries = files.map(file => {
        const debt = Object.fromEntries(MEASURES.filter(measure => debts.get(file)[measure]).map(measure => [
            measure,
            debts.get(file)[measure],
        ]));
        return `    ${JSON.stringify(file)}: ${JSON.stringify(debt)}`;
    });
    writeFileSync(path, `{\n  "files": {\n${entries.join(',\n')}\n  }\n}\n`);
}
