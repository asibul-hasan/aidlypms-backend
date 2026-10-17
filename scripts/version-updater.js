module.exports.readVersion = function (contents) {
  const match = contents.match(/<Version>([0-9]+\.[0-9]+\.[0-9]+)<\/Version>/);
  return match ? match[1] : '1.0.0';
};

module.exports.writeVersion = function (contents, version) {
  if (/<Version>[0-9]+\.[0-9]+\.[0-9]+<\/Version>/.test(contents)) {
    return contents.replace(/<Version>[0-9]+\.[0-9]+\.[0-9]+<\/Version>/, `<Version>${version}</Version>`);
  }
  return contents.replace('</PropertyGroup>', `  <Version>${version}</Version>\n  </PropertyGroup>`);
};
