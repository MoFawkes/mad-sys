import React from 'react';
import { act, create } from 'react-test-renderer';
import { fetchAvailability } from '@/src/listening/broadcastStatus';
import { useLiveNaseehah } from '@/src/listening/useLiveNaseehah';

jest.mock('react-native', () => ({ AppState: { currentState: 'active', addEventListener: jest.fn(() => ({ remove: jest.fn() })) } }));
jest.mock('@/src/listening/broadcastStatus', () => ({ fetchAvailability: jest.fn() }));

describe('Naseehah live promotion', () => {
  let tree: ReturnType<typeof create>;
  let promoted = false;
  const fetchStatus = jest.mocked(fetchAvailability);
  function Probe({ eligible }: { eligible: boolean }) {
    promoted = useLiveNaseehah(eligible);
    return null;
  }
  beforeEach(() => { jest.useFakeTimers(); fetchStatus.mockReset(); promoted = false; });
  afterEach(async () => { if (tree) await act(async () => tree.unmount()); jest.useRealTimers(); });

  it('checks only during Naseehah and removes LIVE immediately when lessons resume', async () => {
    fetchStatus.mockResolvedValue(true);
    await act(async () => { tree = create(<Probe eligible={false} />); });
    expect(fetchStatus).not.toHaveBeenCalled();
    expect(promoted).toBe(false);
    await act(async () => tree.update(<Probe eligible />));
    expect(promoted).toBe(true);
    await act(async () => tree.update(<Probe eligible={false} />));
    expect(promoted).toBe(false);
    await act(async () => jest.advanceTimersByTime(120_000));
    expect(fetchStatus).toHaveBeenCalledTimes(1);
  });

  it('does not let a late response promote listening after Naseehah ends', async () => {
    let finish!: (value: boolean) => void;
    fetchStatus.mockImplementation(() => new Promise((resolve) => { finish = resolve; }));
    await act(async () => { tree = create(<Probe eligible />); });
    await act(async () => tree.update(<Probe eligible={false} />));
    await act(async () => finish(true));
    expect(promoted).toBe(false);
  });

  it('removes a live badge when the next minute check is unknown', async () => {
    fetchStatus.mockResolvedValueOnce(true).mockResolvedValueOnce(null);
    await act(async () => { tree = create(<Probe eligible />); });
    expect(promoted).toBe(true);
    await act(async () => jest.advanceTimersByTime(60_000));
    expect(promoted).toBe(false);
  });
});
