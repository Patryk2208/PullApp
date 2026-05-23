import { Text, StyleSheet } from 'react-native';

export function FormError({ message }: { message: string | null }) {
	if (!message) return null;
	return <Text style={styles.error}>{message}</Text>;
}

const styles = StyleSheet.create({
	error: { color: '#c0392b', fontSize: 13 },
});