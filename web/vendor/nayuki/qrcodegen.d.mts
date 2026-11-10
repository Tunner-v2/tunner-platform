/** Minimal typed interface for the pinned MIT community encoder; generated JS retains its full notice. */
export declare namespace qrcodegen {
  class QrCode {
    readonly size: number;
    readonly version: number;
    getModule(x: number, y: number): boolean;
    static encodeText(text: string, ecl: QrCode.Ecc): QrCode;
  }
  namespace QrCode {
    class Ecc {
      static readonly MEDIUM: Ecc;
    }
  }
}
