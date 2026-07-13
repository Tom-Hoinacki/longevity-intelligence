import { StatusBar } from "expo-status-bar";
import { Platform, SafeAreaView, StyleSheet, Text, View } from "react-native";
import { createHelloWorldPayload } from "@longevity/shared";

export default function Index() {
  const platform = Platform.OS === "ios" ? "ios" : Platform.OS === "web" ? "web" : "android";
  const payload = createHelloWorldPayload(platform);

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar style="light" />
      <View style={styles.card}>
        <Text style={styles.eyebrow}>{payload.platform} foundation</Text>
        <Text accessibilityRole="header" style={styles.title}>{payload.appName}</Text>
        <Text style={styles.tagline}>{payload.tagline}</Text>
        <Text style={styles.message}>{payload.message}</Text>
        <View style={styles.status} accessibilityRole="text">
          <View style={styles.dot} />
          <Text style={styles.statusText}>Hello World is running.</Text>
        </View>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: "#07111f" },
  card: { flex: 1, justifyContent: "center", padding: 28 },
  eyebrow: { color: "#7ee0c2", fontSize: 12, fontWeight: "700", letterSpacing: 2, textTransform: "uppercase" },
  title: { color: "#f5f7fb", fontSize: 56, fontWeight: "800", letterSpacing: -3, marginTop: 16, maxWidth: 340 },
  tagline: { color: "#a6b7ce", fontSize: 20, lineHeight: 30, marginTop: 18, maxWidth: 360 },
  message: { color: "#a6b7ce", fontSize: 16, lineHeight: 25, marginTop: 30, maxWidth: 360 },
  status: { alignItems: "center", alignSelf: "flex-start", backgroundColor: "rgba(126, 224, 194, 0.1)", borderRadius: 999, flexDirection: "row", gap: 10, marginTop: 38, paddingHorizontal: 16, paddingVertical: 12 },
  dot: { backgroundColor: "#36b992", borderRadius: 5, height: 10, width: 10 },
  statusText: { color: "#7ee0c2", fontSize: 14, fontWeight: "700" },
});
