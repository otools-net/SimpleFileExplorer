import { fakeCanvases } from '../../testing/fakes';
import { CanvasTooLargeError, describeEdit, EDIT_KINDS, EDITOR_TOOLS, editedSize, encode, EditOperation, outputType, renderEdits } from './edits';

describe('edits', () => {
    const crop = (x: number, y: number, width: number, height: number): EditOperation => ({ kind: 'crop', rect: { x, y, width, height } });

    afterEach(() => vi.restoreAllMocks());

    it('has a handler and a tool for every kind of edit', () => {
        expect(Object.keys(EDIT_KINDS).sort()).toEqual(EDITOR_TOOLS.map(tool => tool.id).sort());
        expect(describeEdit(crop(0, 0, 800, 600))).toBe('Crop to 800 × 600');
    });

    it('computes the size after the edits, in turn', () => {
        expect(editedSize({ width: 4000, height: 3000 }, [])).toEqual({ width: 4000, height: 3000 });
        expect(editedSize({ width: 4000, height: 3000 }, [crop(10, 10, 2000, 1000), crop(0, 0, 500, 400)])).toEqual({ width: 500, height: 400 });
    });

    it('draws each edit on the result of the ones before', () => {
        const { drawn } = fakeCanvases();
        const source = document.createElement('img');

        const result = renderEdits(source, { width: 4000, height: 3000 }, [crop(100, 200, 2000, 1000), crop(10, 20, 30, 40)]);

        expect([result.width, result.height]).toEqual([30, 40]);
        expect(drawn).toHaveLength(2);
        expect(drawn[0].slice(1)).toEqual([source, 100, 200, 2000, 1000, 0, 0, 2000, 1000]);
        expect(drawn[1][1]).toBe(drawn[0][0]);
        expect(drawn[1].slice(2)).toEqual([10, 20, 30, 40, 0, 0, 30, 40]);
        expect(drawn[1][0]).toBe(result);
    });

    it('copies the picture without edits', () => {
        const { drawn } = fakeCanvases();
        const source = document.createElement('img');

        const result = renderEdits(source, { width: 64, height: 48 }, []);

        expect([result.width, result.height]).toEqual([64, 48]);
        expect(drawn[0].slice(1)).toEqual([source, 0, 0]);
    });

    it('says when the browser cannot draw a canvas that large', () => {
        vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null);

        expect(() => renderEdits(document.createElement('img'), { width: 30000, height: 30000 }, [])).toThrow(CanvasTooLargeError);
    });

    it('saves in the format of the original when a canvas writes it, else in PNG', () => {
        expect(outputType('photo.JPG')).toBe('image/jpeg');
        expect(outputType('a.b.jpeg')).toBe('image/jpeg');
        expect(outputType('photo.webp')).toBe('image/webp');
        expect(outputType('photo.png')).toBe('image/png');
        expect(outputType('scan.tiff')).toBe('image/png');
        expect(outputType('anim.gif')).toBe('image/png');
        expect(outputType('no-extension')).toBe('image/png');
    });

    it('encodes with the quality as a fraction', async () => {
        fakeCanvases();
        const canvas = document.createElement('canvas');
        const toBlob = vi.mocked(HTMLCanvasElement.prototype.toBlob);

        const blob = await encode(canvas, 'image/jpeg', 92);

        expect(blob.type).toBe('image/jpeg');
        expect(toBlob).toHaveBeenCalledWith(expect.any(Function), 'image/jpeg', 0.92);
    });

    it('rejects when the browser encodes nothing', async () => {
        vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(callback => callback(null));

        await expect(encode(document.createElement('canvas'), 'image/png', 90)).rejects.toThrow(CanvasTooLargeError);
    });
});
