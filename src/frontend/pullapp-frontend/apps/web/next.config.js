module.exports = {
  reactStrictMode: true,
  output: 'standalone',

  async rewrites() {
    return [
      { source: '/api/:path*', destination: 'http://127.0.0.1:8080/api/:path*' },
      { source: '/sse/:path*', destination: 'http://127.0.0.1:8080/sse/:path*' },
    ];
  },

  turbopack: {
    resolveAlias: {
      "react-native": "react-native-web",
    },
    resolveExtensions: [
      ".web.js",
      ".web.jsx",
      ".web.ts",
      ".web.tsx",
      ".js",
      ".jsx",
      ".ts",
      ".tsx",
      ".json",
    ],
  },
  webpack: (config) => {
    config.resolve.alias = {
      ...(config.resolve.alias || {}),
      // Transform all direct `react-native` imports to `react-native-web`
      "react-native$": "react-native-web",
    };
    config.resolve.extensions = [
      ".web.js",
      ".web.jsx",
      ".web.ts",
      ".web.tsx",
      ...config.resolve.extensions,
    ];
    return config;
  },
};