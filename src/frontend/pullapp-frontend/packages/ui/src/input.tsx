'use client';
import { TextInput, View, Text, StyleSheet, TextInputProps } from 'react-native';

interface InputProps extends TextInputProps {
	label: string;
}

export function Input({ label, ...props }: InputProps) {
	return (
		<View style={styles.wrapper}>
			<Text style={styles.label}>{label}</Text>
			<TextInput style={styles.input} placeholderTextColor="#888" {...props} />
		</View>
	);
}

const styles = StyleSheet.create({
	wrapper: { gap: 4 },
	label:   { fontSize: 14, color: '#111' },
	input:   {
		borderWidth: 1, borderColor: '#ccc', borderRadius: 8,
		paddingHorizontal: 12, paddingVertical: 10, fontSize: 16,
	},
});
