import { useEffect, useState } from 'react';
import { AppState } from 'react-native';

import { fetchAvailability } from './broadcastStatus';

export function useLiveNaseehah(isNaseehah: boolean): boolean {
  const [live, setLive] = useState(false);
  const [appState, setAppState] = useState(AppState.currentState);

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state) => { setLive(false); setAppState(state); });
    return () => subscription.remove();
  }, []);

  useEffect(() => {
    setLive(false);
    if (!isNaseehah || appState !== 'active') return;
    let disposed = false;
    let checking = false;
    let request: AbortController | undefined;
    const check = async () => {
      if (checking) return;
      checking = true;
      request = new AbortController();
      const timeout = setTimeout(() => request?.abort(), 8000);
      const available = await fetchAvailability(request.signal);
      clearTimeout(timeout);
      if (!disposed) setLive(available === true);
      checking = false;
    };
    void check();
    const timer = setInterval(() => { void check(); }, 60_000);
    return () => { disposed = true; clearInterval(timer); request?.abort(); };
  }, [isNaseehah, appState]);

  return isNaseehah && appState === 'active' && live;
}
