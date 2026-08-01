import { router } from 'expo-router';
import { useState } from 'react';
import { Alert, StyleSheet, Text, View } from 'react-native';

import { getSupabaseClient } from '@/src/data/sessionStore';
import { Field, PrimaryButton } from '@/src/ui/components';
import { theme } from '@/src/ui/theme';

export default function ResetPasswordScreen() {
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);

  async function save() {
    setBusy(true);
    const { error } = await getSupabaseClient().auth.updateUser({ password });
    setBusy(false);
    if (error) {
      Alert.alert('Could not change password', error.message);
      return;
    }
    Alert.alert('Password changed');
    router.replace('/(tabs)/clock');
  }

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Choose a new password</Text>
      <Field
        autoComplete="new-password"
        onChangeText={setPassword}
        placeholder="New password"
        secureTextEntry
        value={password}
      />
      <PrimaryButton disabled={busy || password.length < 10} onPress={save}>
        Save password
      </PrimaryButton>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    backgroundColor: theme.colors.cream,
    flex: 1,
    gap: theme.spacing.md,
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },
  title: { color: theme.colors.navy, fontSize: 28, fontWeight: '700' },
});
