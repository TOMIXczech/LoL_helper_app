import re
html = open(r'C:\Users\ufo\.gemini\antigravity\brain\f6668657-38c9-48c0-a36c-5fd1a83eab6a\.system_generated\steps\2\content.md', encoding='utf-8').read()
urls = re.findall(r'data-url="([^"]+)"', html)
for url in urls:
    print(url)
