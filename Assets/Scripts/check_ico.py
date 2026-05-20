from PIL import Image
img = Image.open(r"C:\Users\Maild\Documents\Coding\mono media player\Assets\Icons\mono.ico")
sizes = img.info.get("sizes", "N/A")
print(f"Sizes: {sizes}")
for s in sizes:
    print(f"  {s[0]}x{s[1]}")
