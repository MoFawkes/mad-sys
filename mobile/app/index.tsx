import { Redirect } from 'expo-router';
import { ActivityIndicator, StyleSheet, View } from 'react-native';

import { useApp } from '@/src/ui/AppProvider';
import { theme } from '@/src/ui/theme';

export default function IndexScreen() {
  const { session } = useApp();
  if (session.status === 'loading') {
    return (
      <View style={styles.loading}>
        <ActivityIndicator color={theme.colors.cream} size="large" />
      </View>
    );
  }
  if (
    session.status === 'signedIn' &&
    session.mode === 'student' &&
    !session.selectionComplete
  ) {
    return <Redirect href="/student-setup" />;
  }
  return (
    <Redirect href={session.status === 'signedIn' ? '/(tabs)/clock' : '/role-choice'} />
  );
}

const styles = StyleSheet.create({
  loading: {
    alignItems: 'center',
    backgroundColor: theme.colors.navy,
    flex: 1,
    justifyContent: 'center',
  },
});
