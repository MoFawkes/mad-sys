import { fetchAvailability, parseAvailability } from '@/src/listening/broadcastStatus';

describe('official eMasjid player availability', () => {
  it.each([
    ['<small class="text-light status-label">Currently Offline</small>', false],
    ["<small class='text-danger status-label'>Currently Offline</small>", false],
    ['<audio controls id="track"></audio>', true],
    ["<AUDIO id = 'track'></AUDIO>", true],
    ['<script>document.getElementById("track")</script>', null],
    ['Service unavailable', null],
    ["<audio id='track'></audio><small class='status-label'>Currently Offline</small>", false],
  ])('reads status without requesting audio: %s', (html, expected) => {
    expect(parseAvailability(html as string)).toBe(expected);
  });

  it('treats network and server failures as unknown', async () => {
    const originalFetch = global.fetch;
    try {
      global.fetch = jest.fn().mockRejectedValue(new Error('Offline'));
      expect(await fetchAvailability(new AbortController().signal)).toBeNull();
      global.fetch = jest.fn().mockResolvedValue({ ok: false });
      expect(await fetchAvailability(new AbortController().signal)).toBeNull();
    } finally { global.fetch = originalFetch; }
  });
});
