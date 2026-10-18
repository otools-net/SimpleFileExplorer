// A small PDF for the docs' screenshots, written by hand: pages of text in Helvetica (one of the standard fonts, not
// embedded: pdf.js loads it from the app's pdfjs/standard_fonts), a title and an outline with one entry per page.
// Each page: { heading, lines }.

function escape(text) {
    return text.replace(/\\/g, '\\\\').replace(/\(/g, '\\(').replace(/\)/g, '\\)');
}

/** The PDF bytes (latin-1 text only) for `pages`, titled `title`. */
export function makePdf(title, pages) {
    const objects = [];
    const add = body => {
        objects.push(body);
        return objects.length;
    };
    const catalog = add(null);
    const pagesId = add(null);
    const font = add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>');
    const bold = add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>');
    const outlines = add(null);
    const pageIds = [];
    for (const page of pages) {
        let content = `BT /F2 22 Tf 72 720 Td (${escape(page.heading)}) Tj ET\n`;
        let y = 680;
        for (const line of page.lines) {
            content += `BT /F1 12 Tf 72 ${y} Td (${escape(line)}) Tj ET\n`;
            y -= 20;
        }
        const stream = add(`<< /Length ${content.length} >>\nstream\n${content}endstream`);
        pageIds.push(add(`<< /Type /Page /Parent ${pagesId} 0 R /MediaBox [0 0 612 792] /Contents ${stream} 0 R /Resources << /Font << /F1 ${font} 0 R /F2 ${bold} 0 R >> >> >>`));
    }
    const items = pages.map((page, index) => add(null));
    items.forEach((id, index) => {
        const links = [`/Title (${escape(pages[index].heading)})`, `/Parent ${outlines} 0 R`, `/Dest [${pageIds[index]} 0 R /XYZ 0 792 null]`];
        if (index > 0)
            links.push(`/Prev ${items[index - 1]} 0 R`);
        if (index < items.length - 1)
            links.push(`/Next ${items[index + 1]} 0 R`);
        objects[id - 1] = `<< ${links.join(' ')} >>`;
    });
    objects[outlines - 1] = `<< /Type /Outlines /First ${items[0]} 0 R /Last ${items.at(-1)} 0 R /Count ${items.length} >>`;
    objects[pagesId - 1] = `<< /Type /Pages /Kids [${pageIds.map(id => `${id} 0 R`).join(' ')}] /Count ${pageIds.length} >>`;
    objects[catalog - 1] = `<< /Type /Catalog /Pages ${pagesId} 0 R /Outlines ${outlines} 0 R /PageMode /UseOutlines >>`;
    const info = add(`<< /Title (${escape(title)}) /Producer (SFE docs) >>`);

    let pdf = '%PDF-1.4\n';
    const offsets = [];
    objects.forEach((body, index) => {
        offsets.push(pdf.length);
        pdf += `${index + 1} 0 obj\n${body}\nendobj\n`;
    });
    const xref = pdf.length;
    pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
    for (const offset of offsets)
        pdf += `${String(offset).padStart(10, '0')} 00000 n \n`;
    pdf += `trailer\n<< /Size ${objects.length + 1} /Root ${catalog} 0 R /Info ${info} 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
    return Buffer.from(pdf, 'latin1');
}
