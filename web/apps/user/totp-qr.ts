import { qrcodegen } from '../../vendor/nayuki/qrcodegen.mjs';

export type QrMatrixResult = { readonly ok: true; readonly extent: number; readonly path: string }
  | { readonly ok: false; readonly code: 'QR_UNAVAILABLE' };

/** Encode locally. Never return or log the input; callers treat the matrix as equally sensitive. */
export function encodeQrMatrix(value: string): QrMatrixResult {
  try {
    const code = qrcodegen.QrCode.encodeText(value, qrcodegen.QrCode.Ecc.MEDIUM);
    const quietZone = 4;
    let path = '';
    for (let y = 0; y < code.size; y++) for (let x = 0; x < code.size; x++) {
      if (code.getModule(x, y)) path += 'M' + (x + quietZone) + ',' + (y + quietZone) + 'h1v1h-1z';
    }
    return { ok: true, extent: code.size + quietZone * 2, path };
  } catch {
    return { ok: false, code: 'QR_UNAVAILABLE' };
  }
}
