import http from 'node:http';
import { Readable } from 'node:stream';

const PORT = Number(process.env.PORT || 18080);
const TOTAL = 220 * 1024 * 1024;
const BLOCK = Buffer.alloc(64 * 1024);
for (let i = 0; i < BLOCK.length; i++) BLOCK[i] = (i * 31 + 17) % 256;
const ETAG = '"lumen-test-v1"';
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function parseRange(header, total) {
  const match = /^bytes=(\d+)-(\d*)$/.exec(header || '');
  if (!match) return null;
  const start = Number(match[1]);
  let end = match[2] ? Number(match[2]) : total - 1;
  if (!Number.isFinite(start) || !Number.isFinite(end) || start < 0 || start >= total) return null;
  end = Math.min(end, total - 1);
  return { start, end };
}

const server = http.createServer(async (req, res) => {
  try {
    if (req.method !== 'GET' && req.method !== 'HEAD') {
      res.writeHead(405).end();
      return;
    }
    const range = parseRange(req.headers.range, TOTAL);
    const start = range?.start || 0;
    const end = range?.end ?? TOTAL - 1;
    const length = end - start + 1;
    const headers = {
      'Content-Type': 'application/octet-stream',
      'Content-Length': String(length),
      'Accept-Ranges': 'bytes',
      'ETag': ETAG,
      'Last-Modified': 'Fri, 07 Nov 2026 01:00:00 GMT',
      'Content-Disposition': `attachment; filename="lumen-demo.bin"; filename*=UTF-8''${encodeURIComponent('流光极速测试.bin')}`,
      'Cache-Control': 'no-store',
      'Connection': 'keep-alive',
    };
    if (range) headers['Content-Range'] = `bytes ${start}-${end}/${TOTAL}`;
    res.writeHead(range ? 206 : 200, headers);
    if (req.method === 'HEAD') {
      res.end();
      return;
    }

    async function* body() {
      let remaining = length;
      while (remaining > 0) {
        if (res.destroyed) return;
        const count = Math.min(BLOCK.length, remaining);
        yield count === BLOCK.length ? BLOCK : BLOCK.subarray(0, count);
        remaining -= count;
        await delay(21);
      }
    }

    const stream = Readable.from(body());
    res.on('close', () => stream.destroy());
    stream.pipe(res);
    console.log(`range ${start}-${end} (${(length / 1048576).toFixed(1)} MB)`);
  } catch (error) {
    try { res.destroy(); } catch {}
    console.error(error);
  }
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`Test server ready: http://127.0.0.1:${PORT}/demo/lumen-demo.bin`);
});
