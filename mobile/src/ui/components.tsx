import { PropsWithChildren } from 'react';
import {
  Pressable,
  PressableProps,
  StyleSheet,
  Text,
  TextInput,
  TextInputProps,
} from 'react-native';

import { theme } from './theme';

export function PrimaryButton({
  children,
  disabled,
  style,
  ...props
}: PropsWithChildren<PressableProps>) {
  return (
    <Pressable
      {...props}
      disabled={disabled}
      style={({ pressed }) => [
        styles.button,
        disabled && styles.disabled,
        pressed && !disabled && styles.pressed,
        typeof style === 'function' ? style({ pressed }) : style,
      ]}>
      <Text style={styles.buttonText}>{children}</Text>
    </Pressable>
  );
}

export function Field(props: TextInputProps) {
  return (
    <TextInput
      {...props}
      autoCapitalize={props.autoCapitalize ?? 'none'}
      placeholderTextColor={theme.colors.grey}
      style={[styles.field, props.style]}
    />
  );
}

const styles = StyleSheet.create({
  button: {
    alignItems: 'center',
    backgroundColor: theme.colors.blue,
    borderRadius: 10,
    minHeight: 50,
    justifyContent: 'center',
    paddingHorizontal: theme.spacing.lg,
    paddingVertical: 12,
  },
  buttonText: { color: theme.colors.white, fontSize: 17, fontWeight: '700' },
  disabled: { opacity: 0.5 },
  pressed: { opacity: 0.8 },
  field: {
    backgroundColor: theme.colors.white,
    borderColor: theme.colors.fieldBorder,
    borderRadius: 10,
    borderWidth: 1,
    color: theme.colors.navy,
    fontSize: 17,
    minHeight: 52,
    paddingHorizontal: theme.spacing.md,
  },
});
