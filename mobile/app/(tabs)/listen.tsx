import { useState } from 'react';
import { ActivityIndicator, Alert, Linking, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { WebView } from 'react-native-webview';

import { PrimaryButton } from '@/src/ui/components';
import { theme } from '@/src/ui/theme';
import { PLAYER_URL as PLAYER_BASE_URL } from '@/src/listening/broadcastStatus';

const PLAYER_URL = `${PLAYER_BASE_URL}?theme=dark`;

async function openWebsite(url: string) {
  if (!url.startsWith('https://')) return;
  try { await Linking.openURL(url); }
  catch { Alert.alert('Could not open website', 'Please try again.'); }
}

export default function ListenScreen() {
  const [revision, setRevision] = useState(0);
  const [failed, setFailed] = useState(false);

  return (
    <SafeAreaView edges={['top']} style={styles.screen}>
      <View style={styles.heading}>
        <Text style={styles.title}>Listen live</Text>
        <Text style={styles.subtitle}>Al-Qalam Institute · eMasjid Live</Text>
        <Text style={styles.hint}>Press Play when the channel is live.</Text>
      </View>
      {failed ? (
        <View style={styles.error}>
          <Text style={styles.subtitle}>Could not load the player. Check your internet connection and try again.</Text>
        </View>
      ) : (
        <WebView
          key={revision}
          source={{ uri: PLAYER_URL }}
          style={styles.player}
          containerStyle={styles.player}
          startInLoadingState
          renderLoading={() => <ActivityIndicator color={theme.colors.cream} style={styles.loading} />}
          onError={() => setFailed(true)}
          onHttpError={({ nativeEvent }) => { if (nativeEvent.url === PLAYER_URL) setFailed(true); }}
          onRenderProcessGone={() => setFailed(true)}
          mediaPlaybackRequiresUserAction
          allowsInlineMediaPlayback
          onOpenWindow={({ nativeEvent }) => { void openWebsite(nativeEvent.targetUrl); }}
          onShouldStartLoadWithRequest={({ url }) => {
            if (url === 'about:blank' || url.split('?')[0] === PLAYER_URL.split('?')[0]) return true;
            void openWebsite(url);
            return false;
          }}
        />
      )}
      <View style={styles.actions}>
        <PrimaryButton onPress={() => { setFailed(false); setRevision((value) => value + 1); }}>
          Reload player
        </PrimaryButton>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: theme.colors.navy },
  heading: { padding: 24, gap: 8 },
  title: { color: theme.colors.cream, fontSize: 28, fontWeight: '700' },
  subtitle: { color: theme.colors.textOnNavy, fontSize: 16 },
  hint: { color: theme.colors.mutedTextOnNavy, fontSize: 14 },
  player: { flex: 1, backgroundColor: theme.colors.navy },
  loading: { flex: 1, backgroundColor: theme.colors.navy },
  error: { flex: 1, padding: 24, justifyContent: 'center' },
  actions: { padding: 16 },
});
