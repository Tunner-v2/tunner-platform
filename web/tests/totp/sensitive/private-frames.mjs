// Private bounded transport. Errors deliberately contain no input or parser diagnostics.
export function parsePrivateJson(bytes) {
  let text;
  try { text = new TextDecoder('utf-8', {fatal: true}).decode(bytes); } catch { throw new Error('FRAME_INVALID'); }
  let offset = 0;
  const space = () => { while (/[\x20\t\r\n]/.test(text[offset] ?? '\0')) offset++; };
  const string = () => {
    if (text[offset] !== '"') throw new Error('FRAME_INVALID');
    const start = offset++;
    for (;;) {
      const c = text[offset++];
      if (c === undefined || c.charCodeAt(0) < 32) throw new Error('FRAME_INVALID');
      if (c === '\\') { if (text[offset] === undefined) throw new Error('FRAME_INVALID'); offset++; continue; }
      if (c === '"') { try { return JSON.parse(text.slice(start, offset)); } catch { throw new Error('FRAME_INVALID'); } }
    }
  };
  const value = depth => {
    if (depth > 12) throw new Error('FRAME_INVALID');
    space();
    const c = text[offset];
    if (c === '"') return string();
    if (c === '{') {
      offset++; space(); const object = Object.create(null); const seen = new Set();
      if (text[offset] === '}') { offset++; return object; }
      for (;;) {
        space(); const key = string(); if (seen.has(key)) throw new Error('FRAME_INVALID'); seen.add(key);
        space(); if (text[offset++] !== ':') throw new Error('FRAME_INVALID'); object[key] = value(depth + 1); space();
        const next = text[offset++]; if (next === '}') return object; if (next !== ',') throw new Error('FRAME_INVALID');
      }
    }
    if (c === '[') {
      offset++; space(); const array = []; if (text[offset] === ']') { offset++; return array; }
      for (;;) { array.push(value(depth + 1)); if (array.length > 256) throw new Error('FRAME_INVALID'); space(); const next = text[offset++]; if (next === ']') return array; if (next !== ',') throw new Error('FRAME_INVALID'); }
    }
    for (const [token, result] of [['true', true], ['false', false], ['null', null]]) if (text.startsWith(token, offset)) { offset += token.length; return result; }
    const number = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/.exec(text.slice(offset));
    if (!number) throw new Error('FRAME_INVALID'); offset += number[0].length; const result = Number(number[0]); if (!Number.isFinite(result)) throw new Error('FRAME_INVALID'); return result;
  };
  try { const result = value(0); space(); if (offset !== text.length) throw new Error('FRAME_INVALID'); return result; } catch { throw new Error('FRAME_INVALID'); }
}

export class PrivateFrameReader {
  constructor(stream, {maximum = 2048, total = 131072, queue = 2} = {}) {
    this.stream = stream; this.maximum = maximum; this.total = total; this.queueLimit = queue; this.bytes = 0; this.buffer = Buffer.alloc(0); this.frames = []; this.pending = null; this.failed = false; this.ended = false; this.failureReason = null;
    this.onData = data => {
      if (this.failed) return;
      this.bytes += data.length;
      if (this.bytes > this.total || this.buffer.length + data.length > this.maximum * this.queueLimit + 4 * this.queueLimit) { this.fail(); return; }
      this.buffer = Buffer.concat([this.buffer, data]);
      try {
        while (this.buffer.length >= 4) {
          const length = this.buffer.readUInt32BE(0); if (length === 0 || length > this.maximum) throw new Error('FRAME_INVALID');
          if (this.buffer.length < length + 4) break;
          const payload = this.buffer.subarray(4, length + 4); const frame = parsePrivateJson(payload); payload.fill(0); this.buffer = this.buffer.subarray(length + 4);
          if (this.pending) { const pending = this.pending; this.pending = null; pending.resolve(frame); }
          else { if (this.frames.length >= this.queueLimit) throw new Error('FRAME_INVALID'); this.frames.push(frame); }
        }
      } catch { this.fail(); }
    };
    this.onEnd = () => { this.ended = true; if (this.buffer.length || this.pending) this.fail(this.buffer.length ? 'FRAME_INVALID' : 'EOF'); };
    this.onError = () => this.fail();
    stream.on('data', this.onData); stream.on('end', this.onEnd); stream.on('error', this.onError);
  }
  fail(reason = 'FRAME_INVALID') { if (!this.failed) this.failureReason = reason; this.failed = true; this.buffer.fill(0); this.buffer = Buffer.alloc(0); this.frames.length = 0; if (this.pending) { const pending = this.pending; this.pending = null; pending.reject(new Error('FRAME_INVALID')); } }
  async next(timeout = 2000, signal) {
    if (this.failed || signal?.aborted || this.pending) throw new Error('FRAME_INVALID');
    if (this.frames.length) return this.frames.shift(); if (this.ended) throw new Error('FRAME_INVALID');
    let timer; let cancel;
    try {
      return await new Promise((resolve, reject) => {
        const settle = action => result => { clearTimeout(timer); signal?.removeEventListener('abort', cancel); action(result); };
        this.pending = {resolve: settle(resolve), reject: settle(reject)};
        cancel = () => { this.fail('CANCELED'); };
        signal?.addEventListener('abort', cancel, {once: true});
        timer = setTimeout(() => this.fail('TIMEOUT'), timeout);
      });
    } finally { clearTimeout(timer); signal?.removeEventListener('abort', cancel); }
  }
  get drained() { return this.buffer.length === 0 && this.frames.length === 0 && !this.pending; }
  dispose() { this.fail(); this.stream.off('data', this.onData); this.stream.off('end', this.onEnd); this.stream.off('error', this.onError); }
}
export async function writePrivateFrame(stream, value, maximum = 2048, {timeout = 2000, signal} = {}) {
  let frame; let timer; let onError; let onAbort;
  try {
    if (!Number.isSafeInteger(timeout) || timeout < 1 || timeout > 10000 || signal?.aborted) throw new Error('FRAME_INVALID');
    const payload = Buffer.from(JSON.stringify(value), 'utf8');
    if (payload.length === 0 || payload.length > maximum) { payload.fill(0); throw new Error('FRAME_INVALID'); }
    frame = Buffer.alloc(payload.length + 4); frame.writeUInt32BE(payload.length); payload.copy(frame, 4); payload.fill(0);
    await new Promise((resolve, reject) => {
      let settled = false;
      const finish = failed => { if (settled) return; settled = true; clearTimeout(timer); signal?.removeEventListener('abort', onAbort); failed ? reject(new Error('FRAME_INVALID')) : resolve(); };
      onError = () => finish(true);
      onAbort = () => { stream.destroy(); finish(true); };
      stream.on('error', onError); signal?.addEventListener('abort', onAbort, {once: true});
      timer = setTimeout(onAbort, timeout);
      stream.write(frame, error => finish(Boolean(error)));
    });
  } catch { throw new Error('FRAME_INVALID'); }
  finally {
    clearTimeout(timer); signal?.removeEventListener('abort', onAbort);
    if (onError) { if (stream.destroyed && !stream.closed) stream.once('close', () => stream.off('error', onError)); else stream.off('error', onError); }
    frame?.fill(0);
  }
}
